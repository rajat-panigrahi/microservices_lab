using FluentValidation;
using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Api;
using StrategyOps.BuildingBlocks.Correlation;
using StrategyOps.BuildingBlocks.Messaging;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.BuildingBlocks.Time;
using StrategyOps.Issues.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var serviceAssembly = typeof(Program).Assembly;

builder.Services.AddDbContext<IssuesDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Issues") ?? "Data Source=strategyops-issues.db"));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICorrelationContext, HttpCorrelationContext>();
builder.Services.AddSingleton<IClock, SystemClock>();

builder.Services.AddOutbox<IssuesDbContext>();
builder.Services.AddStrategyOpsMessaging<IssuesDbContext>(builder.Configuration, serviceAssembly);

builder.Services.AddEndpoints(serviceAssembly);
builder.Services.AddSliceHandlers(serviceAssembly);
builder.Services.AddValidatorsFromAssembly(serviceAssembly, includeInternalTypes: true);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddHealthChecks();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options => options.SwaggerDoc("v1", new()
{
    Title = "StrategyOps - Issues",
    Version = "v1",
    Description = "Owns issues and their SLAs. Issues appear here on their own when a risk materialises - nobody calls this service to create them."
}));

var app = builder.Build();

app.UseExceptionHandler();

if (!app.Environment.IsEnvironment("Testing"))
{
    await using (var scope = app.Services.CreateAsyncScope())
    {
        await scope.ServiceProvider.GetRequiredService<IssuesDbContext>().Database.MigrateAsync();
    }
}

app.UseSwagger();
app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Issues v1"));

app.MapHealthChecks("/health");
app.MapEndpoints();

app.Run();
