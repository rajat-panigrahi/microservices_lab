using Microsoft.Extensions.Logging;

namespace StrategyOps.BuildingBlocks.Discovery;

/// <summary>
/// Rewrites <c>http://projects-api/projects/123</c> into a real instance address.
/// </summary>
/// <remarks>
/// <para>
/// This is <b>client-side load balancing</b>: the caller holds the list of instances and picks
/// one, rather than a load balancer sitting in the middle. It removes a network hop and a
/// component that can fail, at the cost of every caller needing the registry client.
/// </para>
/// <para>
/// Putting it in a <see cref="DelegatingHandler"/> means no calling code changes. A handler
/// registers the intent once and every <c>HttpClient</c> built from that factory gets it -
/// the same seam ASP.NET Core uses for auth headers, retries and tracing.
/// </para>
/// <para>
/// Round-robin is a deliberate floor, not a recommendation: it ignores how loaded or how
/// close an instance is. Real client-side balancers use least-outstanding-requests or
/// latency-weighted picks. Round-robin is enough to prove the mechanism and simple enough to
/// read in one sitting.
/// </para>
/// </remarks>
public sealed class DiscoveryHttpMessageHandler(
    IServiceRegistryClient registry,
    ILogger<DiscoveryHttpMessageHandler> logger) : DelegatingHandler
{
    private static int _roundRobin = -1;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var uri = request.RequestUri;

        // A host with no dots is a logical service name; anything else is already a real
        // address and is left alone. That keeps the handler safe to add to every client.
        if (uri is not null && !uri.Host.Contains('.') && uri.Host != "localhost")
        {
            var instances = await registry.LookupAsync(uri.Host, cancellationToken);

            if (instances.Count > 0)
            {
                var index = Math.Abs(Interlocked.Increment(ref _roundRobin)) % instances.Count;
                var chosen = instances[index];

                request.RequestUri = new Uri(new Uri(chosen.BaseUrl), uri.PathAndQuery);

                logger.LogDebug("Resolved {ServiceName} to {BaseUrl}", uri.Host, chosen.BaseUrl);
            }
            else
            {
                // Deliberately not an exception. The request will fail with a normal
                // connection error, which the resilience pipeline already handles - and one
                // failure mode is easier to reason about than two.
                logger.LogWarning("No live instances registered for {ServiceName}", uri.Host);
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
