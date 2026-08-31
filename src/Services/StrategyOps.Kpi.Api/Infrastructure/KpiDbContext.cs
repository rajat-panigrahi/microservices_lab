using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Inbox;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.BuildingBlocks.Persistence;
using StrategyOps.Kpi.Api.Domain;

namespace StrategyOps.Kpi.Api.Infrastructure;

public sealed class KpiDbContext(DbContextOptions<KpiDbContext> options)
    : DbContext(options), IOutboxDbContext, IInboxDbContext
{
    public DbSet<KpiScorecard> Scorecards => Set<KpiScorecard>();

    public DbSet<KpiDefinition> Kpis => Set<KpiDefinition>();

    public DbSet<KpiMeasurement> Measurements => Set<KpiMeasurement>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<KpiScorecard>(entity =>
        {
            entity.ToTable("scorecards");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.ProjectCode).HasMaxLength(30).IsRequired();
            entity.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(s => s.ProjectId).IsUnique();
        });

        modelBuilder.Entity<KpiDefinition>(entity =>
        {
            entity.ToTable("kpis");
            entity.HasKey(k => k.Id);
            entity.Property(k => k.Name).HasMaxLength(150).IsRequired();
            entity.Property(k => k.Unit).HasMaxLength(20).IsRequired();
            entity.Property(k => k.Direction).HasConversion<string>().HasMaxLength(20);
            entity.Property(k => k.Rag).HasConversion<string>().HasMaxLength(20);
            entity.Property(k => k.Target).HasPrecision(18, 4);
            entity.Property(k => k.AmberThreshold).HasPrecision(18, 4);
            entity.Property(k => k.LatestValue).HasPrecision(18, 4);
            entity.HasIndex(k => k.ScorecardId);
        });

        modelBuilder.Entity<KpiMeasurement>(entity =>
        {
            entity.ToTable("measurements");
            entity.HasKey(m => m.Id);
            entity.Property(m => m.Value).HasPrecision(18, 4);
            entity.Property(m => m.RecordedBy).HasMaxLength(120);
            entity.HasIndex(m => new { m.KpiId, m.PeriodEndUtc });
        });

        modelBuilder.ConfigureOutbox();
        modelBuilder.ConfigureInbox();
        modelBuilder.ApplyDateTimeOffsetConversions(Database.ProviderName);
    }
}
