using Microsoft.AspNetCore.Http;

namespace StrategyOps.BuildingBlocks.Correlation;

/// <summary>
/// Carries the correlation id onto outbound HTTP calls, so the chain survives the hop.
/// </summary>
/// <remarks>
/// A correlation id that is only ever logged locally is a request id. What makes it a
/// <em>correlation</em> id is that it is propagated - over HTTP here, and over the message
/// bus by the MassTransit filters in Messaging/CorrelationFilters.
/// </remarks>
public sealed class CorrelationHttpMessageHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!request.Headers.Contains(HttpCorrelationContext.HeaderName))
        {
            var correlationId = accessor.HttpContext?.Items[HttpCorrelationContext.HeaderName] as string;

            if (!string.IsNullOrWhiteSpace(correlationId))
            {
                request.Headers.Add(HttpCorrelationContext.HeaderName, correlationId);
            }
        }

        return base.SendAsync(request, cancellationToken);
    }
}
