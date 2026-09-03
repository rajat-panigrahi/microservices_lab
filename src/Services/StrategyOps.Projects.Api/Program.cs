using FluentValidation;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Api;
using StrategyOps.BuildingBlocks.Chaos;
using StrategyOps.BuildingBlocks.Hosting;
using StrategyOps.BuildingBlocks.Correlation;
using StrategyOps.BuildingBlocks.Messaging;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.BuildingBlocks.Time;
using StrategyOps.Projects.Api.Features.Sagas;
using StrategyOps.Projects.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var serviceAssembly = typeof(Program).Assembly;

// ---------------------------------------------------------------------------
// This service's own database. No other service has this connection string.
// ---------------------------------------------------------------------------
builder.Services.AddDbContext<ProjectsDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Projects")
                      ?? "Data Source=strategyops-projects.db"));

// Auth, service discovery, correlation, health, problem details - see ServiceDefaults.
builder.AddStrategyOpsPlatform("projects-api");

// ---------------------------------------------------------------------------
// Outbox plus the bus. Note that swapping the phase 1 logging publisher for
// RabbitMQ was a one-line change here: no handler, aggregate or test moved,
// because they all depend on IOutboxWriter rather than on a transport.
// ---------------------------------------------------------------------------
builder.Services.AddOutbox<ProjectsDbContext>();
builder.Services.AddStrategyOpsMessaging<ProjectsDbContext>(builder.Configuration, serviceAssembly, bus =>
{
    // The project initiation saga lives here, in the service that owns the aggregate whose
    // lifecycle it coordinates. Its state is persisted in this service's database, so a
    // restart mid-initiation resumes rather than stranding the project.
    bus.AddSagaStateMachine<ProjectInitiationSaga, ProjectInitiationState>()
        .EntityFrameworkRepository(repository =>
        {
            repository.ExistingDbContext<ProjectsDbContext>();
            repository.ConcurrencyMode = ConcurrencyMode.Optimistic;
        });
});

// ---------------------------------------------------------------------------
// Vertical slices: endpoints, handlers and validators are all found by convention,
// so adding a feature folder is the only edit a new use case needs.
// ---------------------------------------------------------------------------
builder.Services.AddEndpoints(serviceAssembly);
builder.Services.AddSliceHandlers(serviceAssembly);
builder.Services.AddValidatorsFromAssembly(serviceAssembly, includeInternalTypes: true);

builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddChaos();

// Tagged "ready", so it is checked by /health/ready but NOT by /health (liveness). A
// database blip should take this instance out of rotation, never restart the process.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ProjectsDbContext>("database", tags: ["ready"]);

builder.Services.AddStrategyOpsSwagger(
    "StrategyOps - Projects",
    "Owns strategic objectives and the project lifecycle. Publishes the events the rest of the portfolio reacts to.");

var app = builder.Build();

if (!app.Environment.IsEnvironment("Testing"))
{
    await using (var scope = app.Services.CreateAsyncScope())
    {
        await scope.ServiceProvider.GetRequiredService<ProjectsDbContext>().Database.MigrateAsync();
    }
}

app.UseChaos();
app.UseStrategyOpsPlatform("Projects v1");
app.MapEndpoints();

app.Run();
