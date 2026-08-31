namespace StrategyOps.BuildingBlocks.Inbox;

/// <summary>
/// A record that this service has already handled a given message with a given consumer.
/// </summary>
/// <remarks>
/// The key is (<see cref="MessageId"/>, <see cref="Consumer"/>), not the message id alone.
/// One event often has several consumers inside the same service - a ProjectClosed event
/// might close the risk register and archive the mitigation plan - and each of them has to
/// run exactly once. Keying on the id alone would let whichever consumer ran first suppress
/// the others.
/// </remarks>
public sealed class InboxMessage
{
    public Guid MessageId { get; set; }

    public string Consumer { get; set; } = string.Empty;

    public DateTimeOffset ProcessedAtUtc { get; set; }
}
