using FluentValidation;
using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Api;
using StrategyOps.BuildingBlocks.Correlation;
using StrategyOps.BuildingBlocks.Messaging;
using StrategyOps.BuildingBlocks.Time;
using StrategyOps.Reporting.Api.Features.RebuildReadModel;
using StrategyOps.Reporting.Api.Hubs;
using StrategyOps.Reporting.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var serviceAssembly = typeof(Program).Assembly;

builder.Services.AddDbContext<ReportingDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Reporting") ?? "Data Source=strategyops-reporting.db"));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICorrelationContext, HttpCorrelationContext>();
builder.Services.AddSingleton<IClock, SystemClock>();

// No outbox here: this service consumes events and publishes none. A read model that starts
// emitting its own events has usually stopped being a read model.
builder.Services.AddStrategyOpsMessaging<ReportingDbContext>(builder.Configuration, serviceAssembly);

builder.Services.Configure<UpstreamServices>(builder.Configuration.GetSection(UpstreamServices.SectionName));
builder.Services.AddHttpClient("upstream", client => client.Timeout = TimeSpan.FromSeconds(5));

builder.Services.AddSignalR();
builder.Services.AddSingleton<IPortfolioNotifier, SignalRPortfolioNotifier>();

builder.Services.AddEndpoints(serviceAssembly);
builder.Services.AddSliceHandlers(serviceAssembly);
builder.Services.AddValidatorsFromAssembly(serviceAssembly, includeInternalTypes: true);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddHealthChecks();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options => options.SwaggerDoc("v1", new()
{
    Title = "StrategyOps - Reporting",
    Version = "v1",
    Description = "The CQRS read side: one denormalised row per project, built from the events the other five services publish. Owns no truth, and can be rebuilt at any time."
}));

var app = builder.Build();

app.UseExceptionHandler();

if (!app.Environment.IsEnvironment("Testing"))
{
    await using (var scope = app.Services.CreateAsyncScope())
    {
        await scope.ServiceProvider.GetRequiredService<ReportingDbContext>().Database.MigrateAsync();
    }
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseSwagger();
app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Reporting v1"));

app.MapHealthChecks("/health");
app.MapHub<PortfolioHub>(PortfolioHub.Path);
app.MapEndpoints();

app.Run();
