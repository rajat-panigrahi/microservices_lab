using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using StrategyOps.BuildingBlocks.Api;
using StrategyOps.BuildingBlocks.Auth;
using StrategyOps.BuildingBlocks.Time;
using StrategyOps.Identity.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var serviceAssembly = typeof(Program).Assembly;

builder.Services.AddDbContext<IdentityDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Identity") ?? "Data Source=strategyops-identity.db"));

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

// This service both issues and validates tokens. Validating its own tokens is what protects
// /connect/me; issuing them is what everything else depends on.
builder.Services.AddStrategyOpsAuth(builder.Configuration);

builder.Services.AddEndpoints(serviceAssembly);
builder.Services.AddSliceHandlers(serviceAssembly);
builder.Services.AddValidatorsFromAssembly(serviceAssembly, includeInternalTypes: true);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddHealthChecks();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "StrategyOps - Identity",
        Version = "v1",
        Description = "Issues the JWTs every other service validates. Demo accounts: portfolio.director, project.manager, risk.owner, viewer."
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the access_token from POST /connect/token."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = []
    });
});

var app = builder.Build();

app.UseExceptionHandler();

if (!app.Environment.IsEnvironment("Testing"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    await db.Database.MigrateAsync();

    await IdentitySeeder.SeedAsync(
        db,
        builder.Configuration["Identity:SeedPassword"] ?? IdentitySeeder.DefaultPassword,
        scope.ServiceProvider.GetRequiredService<ILogger<Program>>());
}

app.UseSwagger();
app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Identity v1"));

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapEndpoints();

app.Run();
