using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace StrategyOps.BuildingBlocks.Discovery;

public sealed class DiscoveryOptions
{
    public const string SectionName = "Discovery";

    /// <summary>Turn off to fall back to configured addresses only.</summary>
    public bool Enabled { get; set; } = true;

    public string RegistryUrl { get; set; } = "http://localhost:5108";

    /// <summary>This service's own name in the registry, e.g. "projects-api".</summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>The address other services should call. Must be reachable from them, not just from here.</summary>
    public string SelfUrl { get; set; } = string.Empty;

    public int LeaseSeconds { get; set; } = 30;

    /// <summary>How long a lookup result is reused before asking the registry again.</summary>
    public int CacheSeconds { get; set; } = 10;
}

public sealed record RegisteredInstance(string InstanceId, string ServiceName, string BaseUrl);

public interface IServiceRegistryClient
{
    Task<IReadOnlyList<RegisteredInstance>> LookupAsync(string serviceName, CancellationToken cancellationToken);

    Task<bool> RegisterAsync(string instanceId, CancellationToken cancellationToken);

    Task<bool> HeartbeatAsync(string instanceId, CancellationToken cancellationToken);

    Task DeregisterAsync(string instanceId, CancellationToken cancellationToken);
}

/// <summary>
/// Talks to the Discovery service, with a short cache in front of it.
/// </summary>
/// <remarks>
/// The cache is not an optimisation detail, it is what stops the registry becoming a hard
/// dependency on every single outbound call. With a ten-second cache, the registry can be
/// down for ten seconds and nothing notices; without it, the registry is now on the critical
/// path of every request in the system and is the least reliable thing in it.
///
/// The cost is staleness: for up to ten seconds a caller may try an instance that has just
/// gone away. That is what the retry policy on the HTTP client is for - the two mechanisms
/// are designed together.
/// </remarks>
public sealed class ServiceRegistryClient(
    HttpClient http,
    Microsoft.Extensions.Options.IOptions<DiscoveryOptions> options,
    ILogger<ServiceRegistryClient> logger) : IServiceRegistryClient
{
    private readonly Dictionary<string, (DateTimeOffset ExpiresAt, IReadOnlyList<RegisteredInstance> Instances)> _cache = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<IReadOnlyList<RegisteredInstance>> LookupAsync(string serviceName, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);

        try
        {
            if (_cache.TryGetValue(serviceName, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
            {
                return cached.Instances;
            }

            var instances = await FetchAsync(serviceName, cancellationToken);

            _cache[serviceName] = (DateTimeOffset.UtcNow.AddSeconds(options.Value.CacheSeconds), instances);
            return instances;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> RegisterAsync(string instanceId, CancellationToken cancellationToken)
    {
        var settings = options.Value;

        try
        {
            var response = await http.PostAsJsonAsync(
                $"{settings.RegistryUrl}/registry/instances",
                new
                {
                    InstanceId = instanceId,
                    ServiceName = settings.ServiceName,
                    BaseUrl = settings.SelfUrl,
                    settings.LeaseSeconds
                },
                cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A registry that is down must not stop this service from starting. Callers fall
            // back to configured addresses; registration retries on the next heartbeat.
            logger.LogWarning("Could not register with the service registry: {Message}", ex.Message);
            return false;
        }
    }

    public async Task<bool> HeartbeatAsync(string instanceId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await http.PutAsync(
                $"{options.Value.RegistryUrl}/registry/instances/{instanceId}/heartbeat",
                content: null,
                cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Heartbeat failed: {Message}", ex.Message);
            return false;
        }
    }

    public async Task DeregisterAsync(string instanceId, CancellationToken cancellationToken)
    {
        try
        {
            await http.DeleteAsync($"{options.Value.RegistryUrl}/registry/instances/{instanceId}", cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Deregistration failed: {Message}", ex.Message);
        }
    }

    private async Task<IReadOnlyList<RegisteredInstance>> FetchAsync(string serviceName, CancellationToken cancellationToken)
    {
        try
        {
            var instances = await http.GetFromJsonAsync<List<RegisteredInstance>>(
                $"{options.Value.RegistryUrl}/registry/services/{serviceName}",
                cancellationToken);

            return instances ?? [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Registry lookup for {ServiceName} failed: {Message}", serviceName, ex.Message);
            return [];
        }
    }
}
