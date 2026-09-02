using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;

namespace StrategyOps.BuildingBlocks.Auth;

/// <summary>
/// Carries the caller's bearer token onto outbound service-to-service calls.
/// </summary>
/// <remarks>
/// <para>
/// Without this, an aggregation endpoint authenticates the user at the edge and then calls
/// four services completely anonymously - and every one of them correctly answers 401. The
/// identity has to travel with the request, because each service validates independently and
/// makes its own authorization decision.
/// </para>
/// <para>
/// This is <b>token relay</b>: the same user token is forwarded, so downstream services see
/// the real caller and their real roles. A Viewer calling the aggregation endpoint gets a
/// Viewer's answer from every service - authorization is not something the gateway can decide
/// on everyone else's behalf.
/// </para>
/// <para>
/// The alternative is the <b>client credentials</b> grant, where the gateway calls downstream
/// as itself. That is right for background work with no user in the picture - a scheduled
/// job, an outbox publisher - but wrong here, because it would give every request the
/// gateway's permissions rather than the user's, and downstream logs would show "gateway did
/// this" instead of who actually did. Production systems often combine the two via OAuth's
/// on-behalf-of exchange, which swaps the user token for a downstream-audience token so a
/// leaked token cannot be replayed against a different service.
/// </para>
/// </remarks>
public sealed class BearerTokenForwardingHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Never overwrite a token the caller set deliberately - a background job using its
        // own credentials must not have them replaced by whatever request happens to be in
        // flight on this thread.
        if (request.Headers.Authorization is null)
        {
            var incoming = accessor.HttpContext?.Request.Headers.Authorization.ToString();

            if (!string.IsNullOrWhiteSpace(incoming) && incoming.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", incoming["Bearer ".Length..].Trim());
            }
        }

        return base.SendAsync(request, cancellationToken);
    }
}
