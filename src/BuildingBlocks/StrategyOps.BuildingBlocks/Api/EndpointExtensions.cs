using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace StrategyOps.BuildingBlocks.Api;

public static class EndpointExtensions
{
    /// <summary>Finds every <see cref="IEndpoint"/> in the service's assembly.</summary>
    public static IServiceCollection AddEndpoints(this IServiceCollection services, Assembly assembly)
    {
        var endpoints = assembly.DefinedTypes
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IEndpoint).IsAssignableFrom(t))
            .Select(t => ServiceDescriptor.Transient(typeof(IEndpoint), t));

        services.TryAddEnumerableRange(endpoints);
        return services;
    }

    public static IApplicationBuilder MapEndpoints(this WebApplication app)
    {
        foreach (var endpoint in app.Services.GetRequiredService<IEnumerable<IEndpoint>>())
        {
            endpoint.Map(app);
        }

        return app;
    }

    /// <summary>
    /// Registers slice handlers by convention: any non-abstract class whose name ends in
    /// "Handler". Scoped, because they take a DbContext.
    /// </summary>
    public static IServiceCollection AddSliceHandlers(this IServiceCollection services, Assembly assembly)
    {
        foreach (var handler in assembly.DefinedTypes.Where(t =>
                     t is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false }
                     && t.Name.EndsWith("Handler", StringComparison.Ordinal)))
        {
            services.AddScoped(handler.AsType());
        }

        return services;
    }

    private static void TryAddEnumerableRange(this IServiceCollection services, IEnumerable<ServiceDescriptor> descriptors)
    {
        foreach (var descriptor in descriptors)
        {
            services.Add(descriptor);
        }
    }
}
