using Microsoft.EntityFrameworkCore;

namespace StrategyOps.BuildingBlocks.Outbox;

public static class OutboxModelBuilderExtensions
{
    /// <summary>
    /// Adds the outbox table to a service's own database. Note that it lives in the service
    /// schema, not in a shared one - that is what keeps the write and the message in a single
    /// local transaction, and it is why the outbox pattern does not reintroduce a shared
    /// database between services.
    /// </summary>
    public static ModelBuilder ConfigureOutbox(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox_messages");
            // Keyed by the sequence, not the message id: the key has to be an auto-assigned
            // integer for insertion order to mean anything, and the message id is a Guid
            // generated in application code. The id still has to be unique - it is the
            // consumer's dedup key - so it gets a unique index instead.
            entity.HasKey(m => m.Sequence);
            entity.Property(m => m.Sequence).ValueGeneratedOnAdd();
            entity.HasIndex(m => m.Id).IsUnique();
            entity.Property(m => m.Type).HasMaxLength(400).IsRequired();
            entity.Property(m => m.Payload).IsRequired();
            entity.Property(m => m.CorrelationId).HasMaxLength(100);
            entity.Property(m => m.LastError).HasMaxLength(2000);

            // The publisher's only query: unprocessed rows, in insertion order.
            entity.HasIndex(m => new { m.ProcessedAtUtc, m.Sequence })
                .HasDatabaseName("ix_outbox_pending");
        });

        return modelBuilder;
    }
}
