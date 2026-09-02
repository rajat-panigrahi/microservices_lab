using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using StrategyOps.BuildingBlocks.Auth;
using StrategyOps.BuildingBlocks.Correlation;
using StrategyOps.BuildingBlocks.Discovery;
using StrategyOps.BuildingBlocks.Time;

namespace StrategyOps.BuildingBlocks.Hosting;

/// <summary>
/// The cross-cutting setup every StrategyOps service shares.
/// </summary>
/// <remarks>
/// Nine services would otherwise repeat the same twenty lines of authentication, discovery,
/// health and Swagger wiring nine times - and drift apart the first time one of them is
/// changed and the others are not. Consistency across services is worth more here than the
/// flexibility of letting each one differ: a platform where every service handles auth
/// slightly differently is a platform with a security hole in it somewhere.
///
/// Anything genuinely service-specific - its DbContext, its consumers, its slices - stays in
/// that service's own Program.cs.
/// </remarks>
public static class ServiceDefaults
{
    /// <param name="serviceName">The logical name used in the service registry, e.g. "projects-api".</param>
    public static WebApplicationBuilder AddStrategyOpsPlatform(this WebApplicationBuilder builder, string serviceName)
    {
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddScoped<ICorrelationContext, HttpCorrelationContext>();
        builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

        builder.Services.AddStrategyOpsAuth(builder.Configuration);

        // Self-registration. The service name and its own address come from configuration so
        // the same image can register correctly in compose, in Kubernetes, or locally.
        builder.Services.Configure<DiscoveryOptions>(options =>
        {
            builder.Configuration.GetSection(DiscoveryOptions.SectionName).Bind(options);
            options.ServiceName = string.IsNullOrWhiteSpace(options.ServiceName) ? serviceName : options.ServiceName;
        });

        builder.Services.AddHttpClient<IServiceRegistryClient, ServiceRegistryClient>();
        builder.Services.AddHostedService<ServiceRegistrationService>();

        builder.Services.AddProblemDetails();
        builder.Services.AddHealthChecks();
        builder.Services.AddEndpointsApiExplorer();

        return builder;
    }

    public static void AddStrategyOpsSwagger(this IServiceCollection services, string title, string description)
    {
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo { Title = title, Version = "v1", Description = description });

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Paste the access_token from the Identity service's POST /connect/token."
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                }] = []
            });
        });
    }

    /// <summary>
    /// The shared middleware pipeline. Order is not stylistic:
    /// exception handling outermost so it catches everything, then authentication (who are
    /// you?) strictly before authorisation (are you allowed?).
    /// </summary>
    public static WebApplication UseStrategyOpsPlatform(this WebApplication app, string swaggerTitle)
    {
        app.UseExceptionHandler();

        app.UseSwagger();
        app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", swaggerTitle));

        app.UseAuthentication();
        app.UseAuthorization();

        // Health must stay open: a probe cannot present a token, and a readiness check that
        // returns 401 makes an orchestrator kill a perfectly healthy pod.
        app.MapHealthChecks("/health").AllowAnonymous();
        app.MapHealthChecks("/health/ready").AllowAnonymous();

        return app;
    }
}
