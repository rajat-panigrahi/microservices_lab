using Microsoft.EntityFrameworkCore;

namespace StrategyOps.BuildingBlocks.Inbox;

public static class InboxModelBuilderExtensions
{
    public static ModelBuilder ConfigureInbox(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InboxMessage>(entity =>
        {
            entity.ToTable("inbox_messages");

            // The composite key IS the deduplication guarantee. It is enforced by the
            // database, not by application code, so two instances of this service racing on
            // the same redelivered message cannot both win.
            entity.HasKey(m => new { m.MessageId, m.Consumer });
            entity.Property(m => m.Consumer).HasMaxLength(200);
        });

        return modelBuilder;
    }
}
