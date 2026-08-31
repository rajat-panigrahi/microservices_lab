using Microsoft.EntityFrameworkCore;
using StrategyOps.Benefits.Api.Domain;
using StrategyOps.BuildingBlocks.Inbox;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.BuildingBlocks.Persistence;

namespace StrategyOps.Benefits.Api.Infrastructure;

public sealed class BenefitsDbContext(DbContextOptions<BenefitsDbContext> options)
    : DbContext(options), IOutboxDbContext, IInboxDbContext
{
    public DbSet<BenefitProfile> Profiles => Set<BenefitProfile>();

    public DbSet<BenefitRealisation> Realisations => Set<BenefitRealisation>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BenefitProfile>(entity =>
        {
            entity.ToTable("benefit_profiles");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.ProjectCode).HasMaxLength(30).IsRequired();
            entity.Property(p => p.Name).HasMaxLength(200).IsRequired();
            entity.Property(p => p.Type).HasConversion<string>().HasMaxLength(20);
            entity.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(p => p.AtRiskReason).HasMaxLength(500);
            entity.Property(p => p.ForecastValue).HasPrecision(18, 2);
            entity.Property(p => p.RealisedToDate).HasPrecision(18, 2);
            entity.Ignore(p => p.RealisationPercent);
            entity.HasIndex(p => p.ProjectId).IsUnique();
        });

        modelBuilder.Entity<BenefitRealisation>(entity =>
        {
            entity.ToTable("benefit_realisations");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.ActualValue).HasPrecision(18, 2);
            entity.HasIndex(r => new { r.ProfileId, r.PeriodEndUtc });
        });

        modelBuilder.ConfigureOutbox();
        modelBuilder.ConfigureInbox();
        modelBuilder.ApplyDateTimeOffsetConversions(Database.ProviderName);
    }
}
