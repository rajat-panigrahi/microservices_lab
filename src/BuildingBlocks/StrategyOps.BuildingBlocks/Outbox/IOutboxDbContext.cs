using Microsoft.EntityFrameworkCore;

namespace StrategyOps.BuildingBlocks.Outbox;

/// <summary>
/// Implemented by every service DbContext, so the shared outbox machinery can work against
/// any of them without knowing anything else about that service's schema.
/// </summary>
public interface IOutboxDbContext
{
    DbSet<OutboxMessage> OutboxMessages { get; }
}
