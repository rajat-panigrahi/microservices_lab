namespace StrategyOps.BuildingBlocks.Outbox;

/// <summary>
/// A row in the transactional outbox.
/// </summary>
/// <remarks>
/// This exists to solve the dual-write problem: a handler that saves a Project to its
/// database and then publishes to RabbitMQ can crash in between, leaving the two out of
/// step forever. Instead the handler writes the entity AND this row in one local
/// transaction, and a background publisher moves the row onto the bus afterwards.
/// The state change and the intent to publish are now atomic; delivery is retried until
/// it succeeds, which is why consumers must be idempotent (see the inbox).
/// </remarks>
public sealed class OutboxMessage
{
    /// <summary>Also the message id on the wire, and therefore the consumer's dedup key.</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Database-assigned insertion order, and the only thing the publisher sorts on.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="OccurredAtUtc"/>. Two events written in the same
    /// millisecond tie, and a clock that steps backwards reorders them - both of which
    /// reorder messages for the same aggregate. A monotonic sequence cannot do either.
    /// </remarks>
    public long Sequence { get; set; }

    /// <summary>Contract type name, resolved back to a CLR type by <see cref="IntegrationEventTypeRegistry"/>.</summary>
    public string Type { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public string CorrelationId { get; set; } = string.Empty;

    public DateTimeOffset OccurredAtUtc { get; set; }

    /// <summary>Null until the publisher has handed the message to the broker.</summary>
    public DateTimeOffset? ProcessedAtUtc { get; set; }

    public int AttemptCount { get; set; }

    public string? LastError { get; set; }
}
