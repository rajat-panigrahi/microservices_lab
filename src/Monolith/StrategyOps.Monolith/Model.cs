using Microsoft.EntityFrameworkCore;

namespace StrategyOps.Monolith;

// ---------------------------------------------------------------------------
// The whole domain, in one file, in one namespace, in one assembly.
//
// This is not a strawman. It is a perfectly reasonable design, and for a team
// of five it is the RIGHT design: one deployable, one database, one
// transaction, no eventual consistency, no message broker, no correlation ids,
// and you can debug it by pressing F5.
//
// Read this next to src/Services/ and the trade becomes concrete rather than
// theological.
// ---------------------------------------------------------------------------

public enum ProjectStage { Draft, Active, OnHold, Closed }

public enum ProjectHealth { Green, Amber, Red }

public enum RiskStatus { Open, Mitigating, Materialised, Closed }

public enum IssueStatus { New, Assigned, Resolved, Closed }

public sealed class StrategicObjective
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Horizon { get; set; } = string.Empty;
}

public sealed class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid ObjectiveId { get; set; }
    public string Sponsor { get; set; } = string.Empty;
    public decimal Budget { get; set; }
    public ProjectStage Stage { get; set; }
    public ProjectHealth Health { get; set; }

    // Navigation properties across what will later become four service
    // boundaries. This is the single biggest thing that makes extraction hard:
    // once any query can join Projects to Risks, every query eventually does.
    public List<Kpi> Kpis { get; set; } = [];
    public List<Risk> Risks { get; set; } = [];
    public List<Issue> Issues { get; set; } = [];
    public BenefitProfile? Benefit { get; set; }
}

public sealed class Kpi
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Target { get; set; }
    public decimal AmberThreshold { get; set; }
    public decimal? LatestValue { get; set; }
}

public sealed class Risk
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Probability { get; set; }
    public int Impact { get; set; }
    public int Score => Probability * Impact;
    public RiskStatus Status { get; set; }
}

public sealed class Issue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid? OriginRiskId { get; set; }
    public string Title { get; set; } = string.Empty;
    public IssueStatus Status { get; set; }
}

public sealed class BenefitProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public decimal ForecastValue { get; set; }
    public decimal RealisedToDate { get; set; }
}

/// <summary>
/// One DbContext for everything. One connection string, one migration history,
/// one deployment, and - crucially - one transaction scope.
/// </summary>
public sealed class StrategyDbContext(DbContextOptions<StrategyDbContext> options) : DbContext(options)
{
    public DbSet<StrategicObjective> Objectives => Set<StrategicObjective>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Kpi> Kpis => Set<Kpi>();
    public DbSet<Risk> Risks => Set<Risk>();
    public DbSet<Issue> Issues => Set<Issue>();
    public DbSet<BenefitProfile> Benefits => Set<BenefitProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Project>().Ignore(p => p.Kpis).Ignore(p => p.Risks).Ignore(p => p.Issues).Ignore(p => p.Benefit);
        modelBuilder.Entity<Risk>().Ignore(r => r.Score);
        modelBuilder.Entity<Project>().Property(p => p.Stage).HasConversion<string>();
        modelBuilder.Entity<Project>().Property(p => p.Health).HasConversion<string>();
        modelBuilder.Entity<Risk>().Property(r => r.Status).HasConversion<string>();
        modelBuilder.Entity<Issue>().Property(i => i.Status).HasConversion<string>();
    }
}
