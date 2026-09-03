using StrategyOps.BuildingBlocks.Domain;

namespace StrategyOps.Discovery.Api.Domain;

/// <summary>
/// One running copy of one service.
/// </summary>
/// <remarks>
/// The registry is deliberately built around a <b>lease</b>, not a registration. An instance
/// says "I am alive, ask me again within N seconds", and if it stops saying so it is evicted.
/// That is the difference between a service registry and a config file: a config file lists
/// what someone thinks is running, a registry lists what has proved it is running in the last
/// few seconds.
///
/// It matters because the interesting failure is not a service that shut down cleanly and
/// deregistered - it is one that was killed, or partitioned off the network, and never got to
/// say goodbye.
/// </remarks>
public sealed class ServiceInstance
{
    private ServiceInstance()
    {
    }

    public string InstanceId { get; private set; } = string.Empty;

    public string ServiceName { get; private set; } = string.Empty;

    public string BaseUrl { get; private set; } = string.Empty;

    public DateTimeOffset RegisteredAtUtc { get; private set; }

    public DateTimeOffset LastHeartbeatUtc { get; private set; }

    public int LeaseSeconds { get; private set; }

    public static ServiceInstance Register(
        string instanceId,
        string serviceName,
        string baseUrl,
        int leaseSeconds,
        DateTimeOffset now)
    {
        Guard.Against(
            leaseSeconds is < 5 or > 300,
            "registry.lease_out_of_range",
            "A lease must be between 5 and 300 seconds.");

        Guard.Against(
            !Uri.TryCreate(baseUrl, UriKind.Absolute, out _),
            "registry.base_url_invalid",
            $"'{baseUrl}' is not an absolute URL.");

        return new ServiceInstance
        {
            InstanceId = Guard.AgainstBlank(instanceId, "registry.instance_id_required", "An instance needs an id."),
            ServiceName = Guard.AgainstBlank(serviceName, "registry.service_name_required", "An instance needs a service name.").ToLowerInvariant(),
            BaseUrl = baseUrl.TrimEnd('/'),
            LeaseSeconds = leaseSeconds,
            RegisteredAtUtc = now,
            LastHeartbeatUtc = now
        };
    }

    public void Heartbeat(DateTimeOffset now) => LastHeartbeatUtc = now;

    /// <summary>
    /// Expired means the lease elapsed with no heartbeat.
    /// </summary>
    /// <remarks>
    /// Note the grace multiplier. Evicting exactly on the lease boundary makes a single
    /// delayed heartbeat - a GC pause, a slow network moment - look identical to a dead
    /// instance, and the registry starts flapping. Production registries all do some version
    /// of this.
    /// </remarks>
    public bool IsExpired(DateTimeOffset now, double graceMultiplier = 2.0) =>
        now > LastHeartbeatUtc.AddSeconds(LeaseSeconds * graceMultiplier);
}
