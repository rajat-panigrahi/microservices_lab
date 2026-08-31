using StrategyOps.Contracts.V1;

namespace StrategyOps.BuildingBlocks.Outbox;

/// <summary>
/// Stages an integration event for publication.
/// </summary>
/// <remarks>
/// Deliberately does NOT save. The caller owns the transaction: it changes the aggregate and
/// enqueues the event, then calls SaveChanges once. That single call is what makes the state
/// change and the message atomic.
/// </remarks>
public interface IOutboxWriter
{
    void Enqueue(IntegrationEvent @event);
}
