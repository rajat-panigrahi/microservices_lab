using StrategyOps.Contracts.V1;

namespace StrategyOps.BuildingBlocks.Outbox;

/// <summary>
/// What the outbox publisher hands messages to.
/// </summary>
/// <remarks>
/// This is the seam that keeps the outbox independent of the transport (the D in SOLID
/// doing real work). Phase 1 runs against <see cref="LoggingIntegrationEventPublisher"/>
/// so the service is useful before RabbitMQ exists; phase 2 swaps in the MassTransit
/// implementation with no change to the outbox, the handlers, or their tests.
/// </remarks>
public interface IIntegrationEventPublisher
{
    Task PublishAsync(IntegrationEvent @event, CancellationToken cancellationToken);
}
