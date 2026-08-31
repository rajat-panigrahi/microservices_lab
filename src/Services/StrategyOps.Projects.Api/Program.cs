using FluentValidation;
using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Api;
using StrategyOps.BuildingBlocks.Correlation;
using StrategyOps.BuildingBlocks.Messaging;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.BuildingBlocks.Time;
using StrategyOps.Projects.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var serviceAssembly = typeof(Program).Assembly;

// ---------------------------------------------------------------------------
// This service's own database. No other service has this connection string.
// ---------------------------------------------------------------------------
builder.Services.AddDbContext<ProjectsDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Projects")
                      ?? "Data Source=strategyops-projects.db"));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICorrelationContext, HttpCorrelationContext>();
builder.Services.AddSingleton<IClock, SystemClock>();

// ---------------------------------------------------------------------------
// Outbox plus the bus. Note that swapping the phase 1 logging publisher for
// RabbitMQ was a one-line change here: no handler, aggregate or test moved,
// because they all depend on IOutboxWriter rather than on a transport.
// ---------------------------------------------------------------------------
builder.Services.AddOutbox<ProjectsDbContext>();
builder.Services.AddStrategyOpsMessaging<ProjectsDbContext>(builder.Configuration, serviceAssembly);

// ---------------------------------------------------------------------------
// Vertical slices: endpoints, handlers and validators are all found by convention,
// so adding a feature folder is the only edit a new use case needs.
// ---------------------------------------------------------------------------
builder.Services.AddEndpoints(serviceAssembly);
builder.Services.AddSliceHandlers(serviceAssembly);
builder.Services.AddValidatorsFromAssembly(serviceAssembly, includeInternalTypes: true);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddHealthChecks();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options => options.SwaggerDoc("v1", new()
{
    Title = "StrategyOps - Projects",
    Version = "v1",
    Description = "Owns strategic objectives and the project lifecycle. Publishes the events the rest of the portfolio reacts to."
}));

var app = builder.Build();

app.UseExceptionHandler();

if (!app.Environment.IsEnvironment("Testing"))
{
    await using (var scope = app.Services.CreateAsyncScope())
    {
        await scope.ServiceProvider.GetRequiredService<ProjectsDbContext>().Database.MigrateAsync();
    }
}

app.UseSwagger();
app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Projects v1"));

app.MapHealthChecks("/health");
app.MapEndpoints();

app.Run();
