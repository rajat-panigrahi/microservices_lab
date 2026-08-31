using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Inbox;
using StrategyOps.BuildingBlocks.Persistence;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.Risk.Api.Domain;

namespace StrategyOps.Risk.Api.Infrastructure;

public sealed class RiskDbContext(DbContextOptions<RiskDbContext> options)
    : DbContext(options), IOutboxDbContext, IInboxDbContext
{
    public DbSet<RiskRegister> Registers => Set<RiskRegister>();

    public DbSet<Domain.Risk> Risks => Set<Domain.Risk>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RiskRegister>(entity =>
        {
            entity.ToTable("risk_registers");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.ProjectCode).HasMaxLength(30).IsRequired();
            entity.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);

            // One register per project, enforced by the database. A redelivered
            // ProjectInitiationRequested must not be able to create a second one.
            entity.HasIndex(r => r.ProjectId).IsUnique();
        });

        modelBuilder.Entity<Domain.Risk>(entity =>
        {
            entity.ToTable("risks");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Title).HasMaxLength(300).IsRequired();
            entity.Property(r => r.Category).HasMaxLength(60).IsRequired();
            entity.Property(r => r.Owner).HasMaxLength(120).IsRequired();
            entity.Property(r => r.MitigationPlan).HasMaxLength(2000);
            entity.Property(r => r.EscalationReason).HasMaxLength(1000);
            entity.Property(r => r.Resolution).HasMaxLength(1000);
            entity.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(r => r.Tier).HasConversion<string>().HasMaxLength(20);

            entity.HasIndex(r => r.RegisterId);
            entity.HasIndex(r => new { r.RegisterId, r.Status });
        });

        modelBuilder.ConfigureOutbox();
        modelBuilder.ConfigureInbox();

        // Must run last: it walks every property the model already knows about.
        modelBuilder.ApplyDateTimeOffsetConversions(Database.ProviderName);
    }
}
