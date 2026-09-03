using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace StrategyOps.BuildingBlocks.Outbox;

/// <summary>
/// Polls the outbox on an interval.
/// </summary>
/// <remarks>
/// Polling is the honest choice for a lab: it is obvious, it survives restarts, and its
/// failure mode is latency rather than lost messages. In production you would usually pair
/// it with change-data-capture (Debezium) or MassTransit's own outbox so the delay is not
/// bounded by the poll interval. The visible lag on the phase 4 dashboard is this interval,
/// and that is the point - eventual consistency you can watch.
/// </remarks>
public sealed class OutboxPublisherService<TDbContext>(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxPublisherService<TDbContext>> logger)
    : BackgroundService
    where TDbContext : DbContext, IOutboxDbContext
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(500);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor<TDbContext>>();

                var dispatched = await processor.DrainOnceAsync(stoppingToken);

                // A full batch probably means more is waiting, so come straight back for it.
                if (dispatched < OutboxProcessor<TDbContext>.BatchSize)
                {
                    await Task.Delay(IdleDelay, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox publisher loop failed; retrying");
                await Task.Delay(IdleDelay, stoppingToken);
            }
        }
    }
}
