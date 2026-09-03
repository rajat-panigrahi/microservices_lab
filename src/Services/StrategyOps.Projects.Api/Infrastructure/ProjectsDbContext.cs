using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Inbox;
using StrategyOps.BuildingBlocks.Persistence;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.Projects.Api.Domain;
using StrategyOps.Projects.Api.Features.Sagas;

namespace StrategyOps.Projects.Api.Infrastructure;

/// <summary>
/// The Projects service's own database. Nothing outside this service opens a connection to
/// it - that is the whole point of database-per-service, and it is why the KPI, Risk and
/// Benefits services have to be told about a new project rather than reading it.
/// </summary>
public sealed class ProjectsDbContext(DbContextOptions<ProjectsDbContext> options)
    : DbContext(options), IOutboxDbContext, IInboxDbContext
{
    public DbSet<Project> Projects => Set<Project>();

    public DbSet<StrategicObjective> Objectives => Set<StrategicObjective>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    /// <summary>
    /// Saga state lives in this service's own database, alongside the projects it
    /// coordinates. That is what lets a saga survive a restart and still know which legs
    /// succeeded - an orchestrator that forgets cannot compensate.
    /// </summary>
    public DbSet<ProjectInitiationState> ProjectInitiations => Set<ProjectInitiationState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StrategicObjective>(entity =>
        {
            entity.ToTable("objectives");
            entity.HasKey(o => o.Id);
            entity.Property(o => o.Code).HasMaxLength(30).IsRequired();
            entity.HasIndex(o => o.Code).IsUnique();
            entity.Property(o => o.Title).HasMaxLength(300).IsRequired();
            entity.Property(o => o.Horizon).HasMaxLength(20).IsRequired();
            entity.Property(o => o.Owner).HasMaxLength(120).IsRequired();
        });

        modelBuilder.Entity<Project>(entity =>
        {
            entity.ToTable("projects");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Code).HasMaxLength(30).IsRequired();
            entity.HasIndex(p => p.Code).IsUnique();
            entity.Property(p => p.Name).HasMaxLength(200).IsRequired();
            entity.Property(p => p.Sponsor).HasMaxLength(120).IsRequired();
            entity.Property(p => p.Budget).HasPrecision(18, 2);

            // Stored as text so the database stays readable and adding a stage does not
            // silently renumber the existing rows.
            entity.Property(p => p.Stage).HasConversion<string>().HasMaxLength(30);
            entity.Property(p => p.Health).HasConversion<string>().HasMaxLength(20);
            entity.Property(p => p.HealthReason).HasMaxLength(500);
            entity.Property(p => p.FailureReason).HasMaxLength(1000);

            entity.HasIndex(p => p.ObjectiveId);
        });

        modelBuilder.Entity<ProjectInitiationState>(entity =>
        {
            entity.ToTable("project_initiation_sagas");
            entity.HasKey(s => s.CorrelationId);
            entity.Property(s => s.CurrentState).HasMaxLength(64).IsRequired();
            entity.Property(s => s.ProjectCode).HasMaxLength(30);
            entity.Property(s => s.FailureReason).HasMaxLength(1000);

            // Optimistic concurrency: three confirmations can land at the same instant, and
            // without this the last write would silently discard the other two flags.
            entity.Property(s => s.Version).IsConcurrencyToken();

            entity.Ignore(s => s.AllProvisioned);
            entity.Ignore(s => s.CompensationComplete);
        });

        modelBuilder.ConfigureOutbox();
        modelBuilder.ConfigureInbox();

        // Must run last: it walks every property the model already knows about.
        modelBuilder.ApplyDateTimeOffsetConversions(Database.ProviderName);
    }
}
