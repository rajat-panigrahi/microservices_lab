using MassTransit;
using StrategyOps.BuildingBlocks.Correlation;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.Contracts.V1;

namespace StrategyOps.BuildingBlocks.Messaging;

/// <summary>
/// Puts an outbox message onto RabbitMQ. This is the only class in the system that knows a
/// broker exists - handlers enqueue, the outbox drains, this publishes.
/// </summary>
public sealed class MassTransitIntegrationEventPublisher(IPublishEndpoint publishEndpoint) : IIntegrationEventPublisher
{
    public Task PublishAsync(IntegrationEvent @event, CancellationToken cancellationToken) =>
        publishEndpoint.Publish(@event, @event.GetType(), context =>
        {
            // Carry OUR message id onto the wire. MassTransit would otherwise assign a fresh
            // one per publish attempt, and a redelivery would then look like a brand new
            // message to every consumer - defeating the inbox entirely.
            context.MessageId = @event.MessageId;
            context.CorrelationId = TryParseCorrelation(@event.CorrelationId);
            context.Headers.Set(HttpCorrelationContext.HeaderName, @event.CorrelationId);
        }, cancellationToken);

    private static Guid? TryParseCorrelation(string correlationId) =>
        Guid.TryParse(correlationId, out var parsed) ? parsed : null;
}
