using Microsoft.AspNetCore.Http;

namespace StrategyOps.BuildingBlocks.Correlation;

/// <summary>
/// The id that ties one user action to every log line and message it produces, across
/// every service it touches. Phase 6 adds the middleware that populates it from the
/// <c>X-Correlation-Id</c> header and the MassTransit filters that carry it over RabbitMQ.
/// </summary>
public interface ICorrelationContext
{
    string CorrelationId { get; }
}

/// <summary>
/// Reads the correlation id stamped onto the request by CorrelationIdMiddleware, and falls
/// back to a fresh id so background work (the outbox publisher) is still traceable.
/// </summary>
public sealed class HttpCorrelationContext(IHttpContextAccessor accessor) : ICorrelationContext
{
    public const string HeaderName = "X-Correlation-Id";

    public string CorrelationId =>
        accessor.HttpContext?.Items[HeaderName] as string
        ?? accessor.HttpContext?.TraceIdentifier
        ?? Guid.NewGuid().ToString("n");
}
