using Microsoft.EntityFrameworkCore;

namespace StrategyOps.BuildingBlocks.Inbox;

public sealed class InboxStore(IInboxDbContext db) : IInboxStore
{
    public async Task<bool> TryClaimAsync(Guid messageId, string consumer, CancellationToken cancellationToken)
    {
        var alreadyHandled = await db.InboxMessages
            .AsNoTracking()
            .AnyAsync(m => m.MessageId == messageId && m.Consumer == consumer, cancellationToken);

        if (alreadyHandled)
        {
            return false;
        }

        db.InboxMessages.Add(new InboxMessage
        {
            MessageId = messageId,
            Consumer = consumer,
            ProcessedAtUtc = DateTimeOffset.UtcNow
        });

        return true;
    }
}
