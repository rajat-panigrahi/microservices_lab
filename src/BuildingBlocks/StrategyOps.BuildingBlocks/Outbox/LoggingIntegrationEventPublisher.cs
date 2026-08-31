using Microsoft.Extensions.Logging;
using StrategyOps.Contracts.V1;

namespace StrategyOps.BuildingBlocks.Outbox;

/// <summary>
/// Stand-in transport used before RabbitMQ is wired up, and in tests that only care that
/// the outbox drained. Writes the event to the log instead of to a broker.
/// </summary>
public sealed class LoggingIntegrationEventPublisher(ILogger<LoggingIntegrationEventPublisher> logger)
    : IIntegrationEventPublisher
{
    public Task PublishAsync(IntegrationEvent @event, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Would publish {EventType} {MessageId} (correlation {CorrelationId})",
            @event.GetType().Name,
            @event.MessageId,
            @event.CorrelationId);

        return Task.CompletedTask;
    }
}
