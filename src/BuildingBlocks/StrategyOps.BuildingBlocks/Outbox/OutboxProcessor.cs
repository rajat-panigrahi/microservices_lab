using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StrategyOps.Contracts.V1;

namespace StrategyOps.BuildingBlocks.Outbox;

/// <summary>
/// Drains pending outbox rows onto the bus. Separated from the hosted service so tests can
/// call one deterministic pass instead of racing a timer.
/// </summary>
public sealed class OutboxProcessor<TDbContext>(
    TDbContext db,
    IIntegrationEventPublisher publisher,
    ILogger<OutboxProcessor<TDbContext>> logger)
    where TDbContext : DbContext, IOutboxDbContext
{
    public const int BatchSize = 50;

    /// <summary>Publishes one batch and returns how many messages were dispatched.</summary>
    public async Task<int> DrainOnceAsync(CancellationToken cancellationToken)
    {
        var pending = await db.OutboxMessages
            .Where(m => m.ProcessedAtUtc == null)
            .OrderBy(m => m.Sequence)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            return 0;
        }

        var dispatched = 0;

        foreach (var message in pending)
        {
            // Ordering matters: messages for the same aggregate must go out in the order they
            // were written, so a failure stops this batch rather than skipping ahead.
            if (!TryDeserialize(message, out var @event))
            {
                message.LastError = $"No contract type registered for '{message.Type}'";
                message.AttemptCount++;
                logger.LogError("Outbox message {MessageId} has unknown type {Type}", message.Id, message.Type);
                break;
            }

            try
            {
                await publisher.PublishAsync(@event, cancellationToken);
                message.ProcessedAtUtc = DateTimeOffset.UtcNow;
                message.LastError = null;
                dispatched++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.AttemptCount++;
                message.LastError = ex.Message;
                logger.LogWarning(
                    ex,
                    "Failed to publish outbox message {MessageId} (attempt {Attempt}); will retry",
                    message.Id,
                    message.AttemptCount);
                break;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return dispatched;
    }

    private static bool TryDeserialize(OutboxMessage message, out IntegrationEvent @event)
    {
        @event = null!;

        if (!IntegrationEventTypeRegistry.TryResolve(message.Type, out var type))
        {
            return false;
        }

        if (JsonSerializer.Deserialize(message.Payload, type, OutboxWriter.SerializerOptions) is not IntegrationEvent deserialized)
        {
            return false;
        }

        @event = deserialized;
        return true;
    }
}
