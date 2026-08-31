using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Inbox;
using StrategyOps.BuildingBlocks.Persistence;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.Issues.Api.Domain;

namespace StrategyOps.Issues.Api.Infrastructure;

public sealed class IssuesDbContext(DbContextOptions<IssuesDbContext> options)
    : DbContext(options), IOutboxDbContext, IInboxDbContext
{
    public DbSet<Issue> Issues => Set<Issue>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Issue>(entity =>
        {
            entity.ToTable("issues");
            entity.HasKey(i => i.Id);
            entity.Property(i => i.Title).HasMaxLength(300).IsRequired();
            entity.Property(i => i.Owner).HasMaxLength(120);
            entity.Property(i => i.ResolutionNotes).HasMaxLength(2000);
            entity.Property(i => i.Severity).HasConversion<string>().HasMaxLength(20);
            entity.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);

            entity.HasIndex(i => i.ProjectId);
            entity.HasIndex(i => new { i.ProjectId, i.Status });

            // One issue per escalated risk, enforced by the database. The choreography can
            // deliver RiskEscalated more than once; this makes a duplicate physically
            // impossible rather than merely unlikely.
            entity.HasIndex(i => i.OriginRiskId)
                .IsUnique()
                .HasFilter(null);
        });

        modelBuilder.ConfigureOutbox();
        modelBuilder.ConfigureInbox();

        // Must run last: it walks every property the model already knows about.
        modelBuilder.ApplyDateTimeOffsetConversions(Database.ProviderName);
    }
}
