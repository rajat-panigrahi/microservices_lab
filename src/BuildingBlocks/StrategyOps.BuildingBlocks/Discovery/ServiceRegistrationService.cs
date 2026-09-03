using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace StrategyOps.BuildingBlocks.Discovery;

/// <summary>
/// Registers this service on startup, heartbeats while it runs, deregisters on shutdown.
/// </summary>
/// <remarks>
/// The heartbeat interval is a third of the lease, so two heartbeats can be lost to a network
/// blip without the instance being evicted. Heartbeating exactly at the lease boundary means
/// every hiccup looks like a death.
///
/// Re-registering when a heartbeat 404s is the case people forget: if the registry restarted,
/// or this instance was evicted during a long GC pause, heartbeating forever into a registry
/// that has never heard of you is silent invisibility.
/// </remarks>
public sealed class ServiceRegistrationService(
    IServiceScopeFactory scopeFactory,
    IOptions<DiscoveryOptions> options,
    ILogger<ServiceRegistrationService> logger) : BackgroundService
{
    // Truncated defensively: a range operator on a string shorter than the bound throws, and
    // whether it does depends on the length of the service and machine names - the kind of
    // bug that passes for one service and fails for another two characters shorter.
    private readonly string _instanceId = Truncate($"{options.Value.ServiceName}-{Environment.MachineName}-{Guid.NewGuid():n}", 48);

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.ServiceName) || string.IsNullOrWhiteSpace(settings.SelfUrl))
        {
            logger.LogInformation("Service discovery is off for this instance");
            return;
        }

        var heartbeatInterval = TimeSpan.FromSeconds(Math.Max(2, settings.LeaseSeconds / 3.0));

        using (var scope = scopeFactory.CreateScope())
        {
            var registry = scope.ServiceProvider.GetRequiredService<IServiceRegistryClient>();

            if (await registry.RegisterAsync(_instanceId, stoppingToken))
            {
                logger.LogInformation("Registered {InstanceId} at {SelfUrl}", _instanceId, settings.SelfUrl);
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(heartbeatInterval, stoppingToken);

                using var scope = scopeFactory.CreateScope();
                var registry = scope.ServiceProvider.GetRequiredService<IServiceRegistryClient>();

                if (!await registry.HeartbeatAsync(_instanceId, stoppingToken))
                {
                    logger.LogWarning("Heartbeat was rejected; re-registering {InstanceId}", _instanceId);
                    await registry.RegisterAsync(_instanceId, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Heartbeat loop failed; continuing");
            }
        }

        // Best-effort clean exit: deregistering means callers stop being handed this address
        // immediately, instead of waiting for the lease to expire.
        using (var scope = scopeFactory.CreateScope())
        {
            var registry = scope.ServiceProvider.GetRequiredService<IServiceRegistryClient>();
            await registry.DeregisterAsync(_instanceId, CancellationToken.None);
        }
    }
}
