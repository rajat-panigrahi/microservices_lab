using FluentValidation;
using StrategyOps.BuildingBlocks.Api;
using StrategyOps.BuildingBlocks.Time;
using StrategyOps.Discovery.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var serviceAssembly = typeof(Program).Assembly;

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IClock, SystemClock>();

// No database: registry state has a lifetime measured in seconds and is rebuilt by the next
// round of heartbeats. Persisting it would buy nothing and put a database on the critical
// path of every lookup.
builder.Services.AddSingleton<ServiceRegistry>();
builder.Services.AddHostedService<RegistryReaper>();

builder.Services.AddEndpoints(serviceAssembly);
builder.Services.AddSliceHandlers(serviceAssembly);
builder.Services.AddValidatorsFromAssembly(serviceAssembly, includeInternalTypes: true);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddHealthChecks();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options => options.SwaggerDoc("v1", new()
{
    Title = "StrategyOps - Discovery",
    Version = "v1",
    Description = "A minimal service registry: register, heartbeat, look up, evict. The readable version of what Consul or Eureka do, and what a Kubernetes Service replaces."
}));

var app = builder.Build();

app.UseExceptionHandler();
app.UseSwagger();
app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Discovery v1"));

app.MapHealthChecks("/health");
app.MapEndpoints();

app.Run();
