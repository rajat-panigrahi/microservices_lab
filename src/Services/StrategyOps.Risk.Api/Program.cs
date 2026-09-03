using FluentValidation;
using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Api;
using StrategyOps.BuildingBlocks.Chaos;
using StrategyOps.BuildingBlocks.Hosting;
using StrategyOps.BuildingBlocks.Correlation;
using StrategyOps.BuildingBlocks.Messaging;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.BuildingBlocks.Time;
using StrategyOps.Risk.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var serviceAssembly = typeof(Program).Assembly;

builder.Services.AddDbContext<RiskDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Risk") ?? "Data Source=strategyops-risk.db"));

// Auth, service discovery, correlation, health, problem details - see ServiceDefaults.
builder.AddStrategyOpsPlatform("risk-api");

builder.Services.AddOutbox<RiskDbContext>();
builder.Services.AddStrategyOpsMessaging<RiskDbContext>(builder.Configuration, serviceAssembly);

builder.Services.AddEndpoints(serviceAssembly);
builder.Services.AddSliceHandlers(serviceAssembly);
builder.Services.AddValidatorsFromAssembly(serviceAssembly, includeInternalTypes: true);

builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddChaos();

// Tagged "ready", so it is checked by /health/ready but NOT by /health (liveness). A
// database blip should take this instance out of rotation, never restart the process.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<RiskDbContext>("database", tags: ["ready"]);

builder.Services.AddStrategyOpsSwagger(
    "StrategyOps - Risk",
    "Owns project risk registers and the 5x5 probability/impact matrix. Escalating a risk starts the choreographed chain into Issues, Projects and Benefits.");

var app = builder.Build();

if (!app.Environment.IsEnvironment("Testing"))
{
    await using (var scope = app.Services.CreateAsyncScope())
    {
        await scope.ServiceProvider.GetRequiredService<RiskDbContext>().Database.MigrateAsync();
    }
}

app.UseChaos();
app.UseStrategyOpsPlatform("Risk v1");
app.MapEndpoints();

app.Run();
