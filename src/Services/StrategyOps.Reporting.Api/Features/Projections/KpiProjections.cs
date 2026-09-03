using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Inbox;
using StrategyOps.BuildingBlocks.Time;
using StrategyOps.Contracts.V1.Kpis;
using StrategyOps.Reporting.Api.Domain;
using StrategyOps.Reporting.Api.Hubs;
using StrategyOps.Reporting.Api.Infrastructure;

namespace StrategyOps.Reporting.Api.Features.Projections;

public sealed class KpiScorecardProvisionedProjection(
    ReportingDbContext db, IInboxStore inbox, IPortfolioNotifier notifier, IClock clock, ILogger<KpiScorecardProvisionedProjection> logger)
    : PortfolioProjection<KpiScorecardProvisioned>(db, inbox, notifier, clock, logger)
{
    protected override Guid ProjectIdOf(KpiScorecardProvisioned message) => message.ProjectId;

    protected override void Apply(PortfolioScorecard scorecard, KpiScorecardProvisioned message)
    {
    }

    protected override async Task ApplyAsync(PortfolioScorecard scorecard, KpiScorecardProvisioned message, CancellationToken cancellationToken)
    {
        scorecard.ProjectCode = message.ProjectCode;
        scorecard.KpiTotal = message.KpiCount;

        // Re-provisioning starts the scorecard over, so any per-KPI status from a previous
        // attempt goes with it.
        await KpiStatusMaintenance.ClearAsync(Db, message.ProjectId, cancellationToken);
        KpiStatusMaintenance.Recount(scorecard, []);
    }
}

public sealed class KpiScorecardWithdrawnProjection(
    ReportingDbContext db, IInboxStore inbox, IPortfolioNotifier notifier, IClock clock, ILogger<KpiScorecardWithdrawnProjection> logger)
    : PortfolioProjection<KpiScorecardWithdrawn>(db, inbox, notifier, clock, logger)
{
    protected override Guid ProjectIdOf(KpiScorecardWithdrawn message) => message.ProjectId;

    protected override void Apply(PortfolioScorecard scorecard, KpiScorecardWithdrawn message)
    {
    }

    protected override async Task ApplyAsync(PortfolioScorecard scorecard, KpiScorecardWithdrawn message, CancellationToken cancellationToken)
    {
        await KpiStatusMaintenance.ClearAsync(Db, message.ProjectId, cancellationToken);
        scorecard.KpiTotal = 0;
        KpiStatusMaintenance.Recount(scorecard, []);
    }
}

/// <summary>
/// Records one KPI's new RAG and recomputes the buckets from scratch.
/// </summary>
/// <remarks>
/// Recomputing from the per-KPI rows rather than incrementing and decrementing counters is
/// deliberate. Counters drift: one missed decrement and the dashboard is wrong forever, with
/// nothing to detect it. Deriving the counts every time means the row is correct as long as
/// the per-KPI rows are, and those are keyed by KPI id so a redelivery simply overwrites.
/// </remarks>
public sealed class KpiMeasurementRecordedProjection(
    ReportingDbContext db, IInboxStore inbox, IPortfolioNotifier notifier, IClock clock, ILogger<KpiMeasurementRecordedProjection> logger)
    : PortfolioProjection<KpiMeasurementRecorded>(db, inbox, notifier, clock, logger)
{
    protected override Guid ProjectIdOf(KpiMeasurementRecorded message) => message.ProjectId;

    protected override void Apply(PortfolioScorecard scorecard, KpiMeasurementRecorded message)
    {
    }

    protected override async Task ApplyAsync(PortfolioScorecard scorecard, KpiMeasurementRecorded message, CancellationToken cancellationToken)
    {
        var status = await Db.KpiStatuses
            .FirstOrDefaultAsync(k => k.ProjectId == message.ProjectId && k.KpiId == message.KpiId, cancellationToken);

        if (status is null)
        {
            status = new ProjectKpiStatus { ProjectId = message.ProjectId, KpiId = message.KpiId };
            Db.KpiStatuses.Add(status);
        }

        status.Rag = message.Rag;

        // Load the rest of this project's statuses so they are all tracked, then count from
        // the change tracker - which includes the row just added or updated above, whereas a
        // fresh query would not see it until SaveChanges.
        await Db.KpiStatuses
            .Where(k => k.ProjectId == message.ProjectId)
            .LoadAsync(cancellationToken);

        KpiStatusMaintenance.Recount(scorecard, CurrentStatuses(message.ProjectId));
    }

    private List<string> CurrentStatuses(Guid projectId) =>
        Db.ChangeTracker.Entries<ProjectKpiStatus>()
            .Where(e => e.Entity.ProjectId == projectId && e.State != EntityState.Deleted)
            .Select(e => e.Entity.Rag)
            .ToList();
}

internal static class KpiStatusMaintenance
{
    public static async Task ClearAsync(ReportingDbContext db, Guid projectId, CancellationToken cancellationToken)
    {
        var existing = await db.KpiStatuses.Where(k => k.ProjectId == projectId).ToListAsync(cancellationToken);
        db.KpiStatuses.RemoveRange(existing);
    }

    /// <summary>Derives the RAG buckets from the per-KPI rows, so the counts cannot drift.</summary>
    public static void Recount(PortfolioScorecard scorecard, IReadOnlyCollection<string> statuses)
    {
        scorecard.KpiGreen = statuses.Count(r => r == "Green");
        scorecard.KpiAmber = statuses.Count(r => r == "Amber");
        scorecard.KpiRed = statuses.Count(r => r == "Red");
        scorecard.KpiNotMeasured = Math.Max(0, scorecard.KpiTotal - scorecard.KpiGreen - scorecard.KpiAmber - scorecard.KpiRed);
    }
}
