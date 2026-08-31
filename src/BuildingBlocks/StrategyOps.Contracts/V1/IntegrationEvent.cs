namespace StrategyOps.Contracts.V1;

/// <summary>
/// Base for every message that crosses a service boundary.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="MessageId"/> is the idempotency key. It is assigned once, when the event is
/// written to the producer's outbox, and it survives every redelivery - so a consumer that
/// has already seen this id can safely drop the message. See the inbox filter in
/// StrategyOps.BuildingBlocks/Inbox.
/// </para>
/// <para>
/// Note that enum-like values (health, severity, tier) are carried as <c>string</c> rather
/// than as shared enums. Adding a value to a shared enum silently breaks consumers compiled
/// against the old one; a string lets an old consumer fall through to a default branch.
/// This is the single most common versioning mistake in event-driven .NET systems.
/// </para>
/// </remarks>
public abstract record IntegrationEvent
{
    /// <summary>Stable, unique id used for consumer-side deduplication.</summary>
    public Guid MessageId { get; init; } = Guid.NewGuid();

    /// <summary>When the producing service committed the state change.</summary>
    public DateTimeOffset OccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Ties this event back to the originating HTTP request across every hop.</summary>
    public string CorrelationId { get; init; } = string.Empty;
}
