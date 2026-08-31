using System.Text.Json;
using StrategyOps.BuildingBlocks.Correlation;
using StrategyOps.Contracts.V1;

namespace StrategyOps.BuildingBlocks.Outbox;

public sealed class OutboxWriter(IOutboxDbContext db, ICorrelationContext correlation) : IOutboxWriter
{
    internal static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public void Enqueue(IntegrationEvent @event)
    {
        var correlationId = string.IsNullOrEmpty(@event.CorrelationId)
            ? correlation.CorrelationId
            : @event.CorrelationId;

        var stamped = @event with { CorrelationId = correlationId };

        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = stamped.MessageId,
            Type = IntegrationEventTypeRegistry.NameOf(stamped),
            Payload = JsonSerializer.Serialize(stamped, stamped.GetType(), SerializerOptions),
            CorrelationId = correlationId,
            OccurredAtUtc = stamped.OccurredAtUtc
        });
    }
}
