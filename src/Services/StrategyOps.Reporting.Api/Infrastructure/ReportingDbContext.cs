using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Inbox;
using StrategyOps.BuildingBlocks.Persistence;
using StrategyOps.Reporting.Api.Domain;

namespace StrategyOps.Reporting.Api.Infrastructure;

/// <summary>
/// The read store. Note there is no outbox: this service publishes nothing, it only reads
/// what others publish. A read model that starts emitting its own events has usually stopped
/// being a read model.
/// </summary>
public sealed class ReportingDbContext(DbContextOptions<ReportingDbContext> options)
    : DbContext(options), IInboxDbContext
{
    public DbSet<PortfolioScorecard> Scorecards => Set<PortfolioScorecard>();

    /// <summary>Per-KPI RAG, so a measurement can be moved between buckets. See ProjectKpiStatus.</summary>
    public DbSet<ProjectKpiStatus> KpiStatuses => Set<ProjectKpiStatus>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PortfolioScorecard>(entity =>
        {
            entity.ToTable("portfolio_scorecards");
            entity.HasKey(s => s.ProjectId);
            entity.Property(s => s.ProjectCode).HasMaxLength(30);
            entity.Property(s => s.ProjectName).HasMaxLength(200);
            entity.Property(s => s.Stage).HasMaxLength(30);
            entity.Property(s => s.Health).HasMaxLength(20);
            entity.Property(s => s.HealthReason).HasMaxLength(500);
            entity.Property(s => s.BenefitStatus).HasMaxLength(20);
            entity.Property(s => s.Budget).HasPrecision(18, 2);
            entity.Property(s => s.BenefitForecast).HasPrecision(18, 2);
            entity.Property(s => s.BenefitRealised).HasPrecision(18, 2);
            entity.Property(s => s.RealisationPercent).HasPrecision(9, 2);

            // Computed from the other columns, so it is never stored.
            entity.Ignore(s => s.OverallStatus);

            entity.HasIndex(s => s.ProjectCode);
            entity.HasIndex(s => s.Stage);
        });

        modelBuilder.Entity<ProjectKpiStatus>(entity =>
        {
            entity.ToTable("project_kpi_statuses");
            entity.HasKey(k => new { k.ProjectId, k.KpiId });
            entity.Property(k => k.Rag).HasMaxLength(20);
        });

        modelBuilder.ConfigureInbox();
        modelBuilder.ApplyDateTimeOffsetConversions(Database.ProviderName);
    }
}
