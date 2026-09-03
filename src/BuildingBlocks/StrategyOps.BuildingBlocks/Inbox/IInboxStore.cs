namespace StrategyOps.BuildingBlocks.Inbox;

public interface IInboxStore
{
    /// <summary>
    /// Stages a claim on (messageId, consumer), returning false if this consumer has already
    /// handled the message.
    /// </summary>
    /// <remarks>
    /// Deliberately does NOT save. The caller commits the inbox row together with whatever
    /// state the message changed, in one transaction - so it is impossible to end up having
    /// done the work without recording it, or vice versa.
    /// </remarks>
    Task<bool> TryClaimAsync(Guid messageId, string consumer, CancellationToken cancellationToken);
}
