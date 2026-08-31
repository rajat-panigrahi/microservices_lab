using FluentValidation;
using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Api;
using StrategyOps.BuildingBlocks.Correlation;
using StrategyOps.BuildingBlocks.Messaging;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.BuildingBlocks.Time;
using StrategyOps.Benefits.Api.Domain;
using StrategyOps.Benefits.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var serviceAssembly = typeof(Program).Assembly;

builder.Services.AddDbContext<BenefitsDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Benefits") ?? "Data Source=strategyops-benefits.db"));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICorrelationContext, HttpCorrelationContext>();
builder.Services.AddSingleton<IClock, SystemClock>();

// The portfolio ceiling is configuration, not a constant: raising it is a business decision,
// and lowering it below a project's forecast is how the compensation demo is triggered.
builder.Services.Configure<PortfolioBenefitPolicy>(
    builder.Configuration.GetSection(PortfolioBenefitPolicy.SectionName));

builder.Services.AddOutbox<BenefitsDbContext>();
builder.Services.AddStrategyOpsMessaging<BenefitsDbContext>(builder.Configuration, serviceAssembly);

builder.Services.AddEndpoints(serviceAssembly);
builder.Services.AddSliceHandlers(serviceAssembly);
builder.Services.AddValidatorsFromAssembly(serviceAssembly, includeInternalTypes: true);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddHealthChecks();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options => options.SwaggerDoc("v1", new()
{
    Title = "StrategyOps - Benefits",
    Version = "v1",
    Description = "Owns benefit forecasts and realisation. Registers a benefit profile as one leg of the project initiation saga - and is the leg that can legitimately refuse, which is what makes compensation observable."
}));

var app = builder.Build();

app.UseExceptionHandler();

if (!app.Environment.IsEnvironment("Testing"))
{
    await using (var scope = app.Services.CreateAsyncScope())
    {
        await scope.ServiceProvider.GetRequiredService<BenefitsDbContext>().Database.MigrateAsync();
    }
}

app.UseSwagger();
app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Benefits v1"));

app.MapHealthChecks("/health");
app.MapEndpoints();

app.Run();
