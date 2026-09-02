using FluentValidation;
using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Api;
using StrategyOps.BuildingBlocks.Chaos;
using StrategyOps.BuildingBlocks.Hosting;
using StrategyOps.BuildingBlocks.Correlation;
using StrategyOps.BuildingBlocks.Messaging;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.BuildingBlocks.Time;
using StrategyOps.Kpi.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var serviceAssembly = typeof(Program).Assembly;

builder.Services.AddDbContext<KpiDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Kpi") ?? "Data Source=strategyops-kpi.db"));

// Auth, service discovery, correlation, health, problem details - see ServiceDefaults.
builder.AddStrategyOpsPlatform("kpi-api");

builder.Services.AddOutbox<KpiDbContext>();
builder.Services.AddStrategyOpsMessaging<KpiDbContext>(builder.Configuration, serviceAssembly);

builder.Services.AddEndpoints(serviceAssembly);
builder.Services.AddSliceHandlers(serviceAssembly);
builder.Services.AddValidatorsFromAssembly(serviceAssembly, includeInternalTypes: true);

builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddChaos();

builder.Services.AddStrategyOpsSwagger(
    "StrategyOps - KPI",
    "Owns KPI scorecards and RAG banding. Provisions a scorecard as one leg of the project initiation saga, and withdraws it again if another leg fails.");

var app = builder.Build();

if (!app.Environment.IsEnvironment("Testing"))
{
    await using (var scope = app.Services.CreateAsyncScope())
    {
        await scope.ServiceProvider.GetRequiredService<KpiDbContext>().Database.MigrateAsync();
    }
}

app.UseChaos();
app.UseStrategyOpsPlatform("Risk v1");
app.MapEndpoints();

app.Run();
