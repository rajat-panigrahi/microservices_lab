using StrategyOps.Discovery.Api.Infrastructure;

namespace StrategyOps.Discovery.Api.Infrastructure;

/// <summary>
/// Evicts instances that stopped sending heartbeats.
/// </summary>
/// <remarks>
/// Without this the registry only ever grows, and it will happily hand out the address of a
/// service that died an hour ago - which turns a clean failure ("nothing is registered") into
/// a confusing one (connection refused, on every third request, for no visible reason).
/// </remarks>
public sealed class RegistryReaper(ServiceRegistry registry, ILogger<RegistryReaper> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var instanceId in registry.Reap())
                {
                    logger.LogWarning("Evicted {InstanceId}: lease expired with no heartbeat", instanceId);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Registry sweep failed");
            }

            try
            {
                await Task.Delay(SweepInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
