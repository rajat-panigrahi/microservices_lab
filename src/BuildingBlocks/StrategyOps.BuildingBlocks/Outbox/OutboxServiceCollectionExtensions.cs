using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace StrategyOps.BuildingBlocks.Outbox;

public static class OutboxServiceCollectionExtensions
{
    /// <summary>
    /// Wires the outbox for a service. <paramref name="runPublisher"/> is false in tests, which
    /// drain the outbox explicitly so assertions are deterministic rather than timing-dependent.
    /// </summary>
    public static IServiceCollection AddOutbox<TDbContext>(this IServiceCollection services, bool runPublisher = true)
        where TDbContext : DbContext, IOutboxDbContext
    {
        services.AddScoped<IOutboxDbContext>(sp => sp.GetRequiredService<TDbContext>());
        services.AddScoped<IOutboxWriter, OutboxWriter>();
        services.AddScoped<OutboxProcessor<TDbContext>>();

        if (runPublisher)
        {
            services.AddHostedService<OutboxPublisherService<TDbContext>>();
        }

        return services;
    }
}
