using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi.Models;
using StrategyOps.BuildingBlocks.Api;
using StrategyOps.BuildingBlocks.Auth;
using StrategyOps.BuildingBlocks.Correlation;
using StrategyOps.BuildingBlocks.Discovery;
using StrategyOps.BuildingBlocks.Observability;
using StrategyOps.BuildingBlocks.Resilience;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
var serviceAssembly = typeof(Program).Assembly;

var resilience = builder.Configuration.GetSection(ResilienceOptions.SectionName).Get<ResilienceOptions>() ?? new ResilienceOptions();

// Structured logging and tracing, tagged as the gateway. The edge is where a correlation id
// is usually born, so it matters that this one logs with it too.
builder.AddStrategyOpsObservability("gateway");

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICorrelationContext, HttpCorrelationContext>();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

// ---------------------------------------------------------------------------
// Authentication at the edge. Every service ALSO validates the token itself -
// see AddStrategyOpsAuth for why checking twice is not redundant.
// ---------------------------------------------------------------------------
builder.Services.AddStrategyOpsAuth(builder.Configuration);

// ---------------------------------------------------------------------------
// Service discovery: outbound calls address services by logical name
// (http://projects-api/...) and the handler resolves a live instance.
// ---------------------------------------------------------------------------
builder.Services.Configure<DiscoveryOptions>(builder.Configuration.GetSection(DiscoveryOptions.SectionName));
builder.Services.AddHttpClient<IServiceRegistryClient, ServiceRegistryClient>();
builder.Services.AddTransient<DiscoveryHttpMessageHandler>();
builder.Services.AddTransient<BearerTokenForwardingHandler>();
builder.Services.AddTransient<CorrelationHttpMessageHandler>();

// One named client per downstream service, so each gets its OWN circuit breaker. Sharing a
// breaker across all of them would mean a sick KPI service tripping the breaker for Risk too,
// which turns partial degradation into total outage - the opposite of the point.
foreach (var service in new[] { "projects", "kpi", "risk", "issues", "benefits", "reporting" })
{
    builder.Services
        .AddHttpClient(service)
        .AddHttpMessageHandler<CorrelationHttpMessageHandler>()
        .AddHttpMessageHandler<BearerTokenForwardingHandler>()
        .AddHttpMessageHandler<DiscoveryHttpMessageHandler>()
        .AddStrategyOpsResilience(resilience);
}

// ---------------------------------------------------------------------------
// Rate limiting at the edge, keyed per user rather than globally, so one noisy
// client cannot consume everyone else's budget.
// ---------------------------------------------------------------------------
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetTokenBucketLimiter(
            context.User.Identity?.Name
                ?? context.Connection.RemoteIpAddress?.ToString()
                ?? "anonymous",
            _ => new TokenBucketRateLimiterOptions
            {
                // A token bucket rather than a fixed window: it allows a short burst (opening
                // a dashboard fires several requests at once) while still capping the
                // sustained rate. A fixed window would reject the burst and then sit idle.
                TokenLimit = 120,
                TokensPerPeriod = 60,
                ReplenishmentPeriod = TimeSpan.FromSeconds(60),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddScoped<StrategyOps.Gateway.Features.PortfolioOverview.PortfolioOverviewHandler>();
builder.Services.AddEndpoints(serviceAssembly);

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "StrategyOps - Gateway",
        Version = "v1",
        Description = "The single front door: routes /api/* to the owning service, validates JWTs at the edge, rate limits per user, and aggregates one project across all five services."
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = []
    });
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseCorrelationId();
app.UseSerilogRequestLogging();
app.UseSwagger();
app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Gateway v1"));

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapEndpoints();

// Everything not handled above is proxied to the owning service by YARP.
app.MapReverseProxy();

app.Run();
