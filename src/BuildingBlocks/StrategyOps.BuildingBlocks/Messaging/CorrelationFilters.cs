using MassTransit;
// MassTransit also defines a LogContext, so this one is aliased rather than imported.
using SerilogContext = Serilog.Context.LogContext;
using StrategyOps.BuildingBlocks.Correlation;

namespace StrategyOps.BuildingBlocks.Messaging;

/// <summary>
/// Carries the correlation id across the broker, in both directions.
/// </summary>
/// <remarks>
/// <para>
/// This is the hop people forget. HTTP propagation is well-trodden; a message sitting in a
/// queue for two seconds and then being handled by a completely different process, on a
/// thread with no HttpContext, is where most correlation chains quietly end.
/// </para>
/// <para>
/// With these filters, escalating a risk and the project going Red three services later share
/// one id - which is exactly the trail you need when the question is "why did this project
/// turn red?".
/// </para>
/// </remarks>
public sealed class CorrelationSendFilter<T> : IFilter<SendContext<T>>
    where T : class
{
    public Task Send(SendContext<T> context, IPipe<SendContext<T>> next)
    {
        // The outbox already stamped the event; this makes it visible as a transport header
        // too, so it can be read in the RabbitMQ management UI without deserialising.
        if (context.Message is Contracts.V1.IntegrationEvent { CorrelationId.Length: > 0 } @event)
        {
            context.Headers.Set(HttpCorrelationContext.HeaderName, @event.CorrelationId);
        }

        return next.Send(context);
    }

    public void Probe(ProbeContext context) => context.CreateFilterScope("correlationSend");
}

public sealed class CorrelationConsumeFilter<T> : IFilter<ConsumeContext<T>>
    where T : class
{
    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        var correlationId =
            context.Headers.Get<string>(HttpCorrelationContext.HeaderName)
            ?? (context.Message as Contracts.V1.IntegrationEvent)?.CorrelationId
            ?? context.CorrelationId?.ToString("n")
            ?? context.MessageId?.ToString("n")
            ?? "unknown";

        using (SerilogContext.PushProperty("CorrelationId", correlationId))
        using (SerilogContext.PushProperty("MessageType", typeof(T).Name))
        {
            await next.Send(context);
        }
    }

    public void Probe(ProbeContext context) => context.CreateFilterScope("correlationConsume");
}
