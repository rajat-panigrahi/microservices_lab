using System.Collections.Concurrent;
using StrategyOps.BuildingBlocks.Time;
using StrategyOps.Discovery.Api.Domain;

namespace StrategyOps.Discovery.Api.Infrastructure;

/// <summary>
/// The registry itself: an in-memory map of service name to live instances.
/// </summary>
/// <remarks>
/// <para>
/// In memory, deliberately. Registry data has a lifetime measured in seconds and is rebuilt
/// by the next round of heartbeats, so persisting it buys nothing and costs a database on the
/// critical path of every lookup.
/// </para>
/// <para>
/// The obvious follow-up is "so what happens when the registry restarts?" - every instance
/// re-registers within one heartbeat interval, and until then lookups return empty and
/// callers fall back to their configured addresses. Which is also the honest answer to why a
/// single-node registry is a single point of failure: real ones (Consul, Eureka) run as a
/// cluster with a consensus protocol precisely because of this.
/// </para>
/// </remarks>
public sealed class ServiceRegistry(IClock clock)
{
    private readonly ConcurrentDictionary<string, ServiceInstance> _instances = new(StringComparer.OrdinalIgnoreCase);

    public ServiceInstance Register(string instanceId, string serviceName, string baseUrl, int leaseSeconds)
    {
        var instance = ServiceInstance.Register(instanceId, serviceName, baseUrl, leaseSeconds, clock.UtcNow);
        _instances[instanceId] = instance;
        return instance;
    }

    /// <summary>Renews a lease. Returns false if the instance was already evicted.</summary>
    public bool Heartbeat(string instanceId)
    {
        if (!_instances.TryGetValue(instanceId, out var instance))
        {
            return false;
        }

        instance.Heartbeat(clock.UtcNow);
        return true;
    }

    public bool Deregister(string instanceId) => _instances.TryRemove(instanceId, out _);

    /// <summary>Live instances of one service, oldest registration first for stable ordering.</summary>
    public IReadOnlyList<ServiceInstance> Healthy(string serviceName)
    {
        var now = clock.UtcNow;

        return _instances.Values
            .Where(i => string.Equals(i.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase) && !i.IsExpired(now))
            .OrderBy(i => i.RegisteredAtUtc)
            .ToList();
    }

    public IReadOnlyList<ServiceInstance> All() =>
        _instances.Values.OrderBy(i => i.ServiceName).ThenBy(i => i.RegisteredAtUtc).ToList();

    /// <summary>Drops expired leases. Returns the instance ids that were evicted.</summary>
    public IReadOnlyList<string> Reap()
    {
        var now = clock.UtcNow;
        var expired = _instances.Values.Where(i => i.IsExpired(now)).Select(i => i.InstanceId).ToList();

        foreach (var instanceId in expired)
        {
            _instances.TryRemove(instanceId, out _);
        }

        return expired;
    }
}
