using Microsoft.EntityFrameworkCore;

namespace StrategyOps.BuildingBlocks.Inbox;

public interface IInboxDbContext
{
    DbSet<InboxMessage> InboxMessages { get; }
}
