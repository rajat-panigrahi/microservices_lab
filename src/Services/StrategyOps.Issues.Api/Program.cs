using FluentValidation;
using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Api;
using StrategyOps.BuildingBlocks.Chaos;
using StrategyOps.BuildingBlocks.Hosting;
using StrategyOps.BuildingBlocks.Correlation;
using StrategyOps.BuildingBlocks.Messaging;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.BuildingBlocks.Time;
using StrategyOps.Issues.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var serviceAssembly = typeof(Program).Assembly;

builder.Services.AddDbContext<IssuesDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Issues") ?? "Data Source=strategyops-issues.db"));

// Auth, service discovery, correlation, health, problem details - see ServiceDefaults.
builder.AddStrategyOpsPlatform("issues-api");

builder.Services.AddOutbox<IssuesDbContext>();
builder.Services.AddStrategyOpsMessaging<IssuesDbContext>(builder.Configuration, serviceAssembly);

builder.Services.AddEndpoints(serviceAssembly);
builder.Services.AddSliceHandlers(serviceAssembly);
builder.Services.AddValidatorsFromAssembly(serviceAssembly, includeInternalTypes: true);

builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddChaos();

builder.Services.AddStrategyOpsSwagger(
    "StrategyOps - Issues",
    "Owns issues and their SLAs. Issues appear here on their own when a risk materialises - nobody calls this service to create them.");

var app = builder.Build();

if (!app.Environment.IsEnvironment("Testing"))
{
    await using (var scope = app.Services.CreateAsyncScope())
    {
        await scope.ServiceProvider.GetRequiredService<IssuesDbContext>().Database.MigrateAsync();
    }
}

app.UseChaos();
app.UseStrategyOpsPlatform("Issues v1");
app.MapEndpoints();

app.Run();
