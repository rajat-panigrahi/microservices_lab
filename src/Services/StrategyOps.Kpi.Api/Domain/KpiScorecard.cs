using StrategyOps.BuildingBlocks.Domain;

namespace StrategyOps.Kpi.Api.Domain;

/// <summary>
/// A project's set of measures. Provisioned by the initiation saga, and withdrawn again if
/// initiation fails.
/// </summary>
public sealed class KpiScorecard
{
    private KpiScorecard()
    {
    }

    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    public string ProjectCode { get; private set; } = string.Empty;

    public Guid ObjectiveId { get; private set; }

    public ScorecardStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static KpiScorecard Provision(Guid projectId, string projectCode, Guid objectiveId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        ProjectId = Guard.AgainstEmpty(projectId, "scorecard.project_required", "A scorecard belongs to a project."),
        ProjectCode = Guard.AgainstBlank(projectCode, "scorecard.project_code_required", "A scorecard needs the project code."),
        ObjectiveId = objectiveId,
        Status = ScorecardStatus.Active,
        CreatedAtUtc = now
    };

    public void Close() => Status = ScorecardStatus.Closed;

    public void EnsureAcceptingMeasurements() =>
        Guard.Against(
            Status == ScorecardStatus.Closed,
            "scorecard.closed",
            "This project's scorecard is closed; no further measurements can be recorded.");

    /// <summary>
    /// The measures every project gets on day one. Seeding these is what makes "provision a
    /// scorecard" a real unit of work with something to compensate, rather than an empty row.
    /// </summary>
    public static IEnumerable<KpiDefinition> BaselineKpisFor(Guid scorecardId)
    {
        yield return KpiDefinition.Create(scorecardId, "Schedule variance", "%", KpiDirection.HigherIsBetter, target: 0m, amberThreshold: -5m);
        yield return KpiDefinition.Create(scorecardId, "Cost variance", "%", KpiDirection.HigherIsBetter, target: 0m, amberThreshold: -5m);
        yield return KpiDefinition.Create(scorecardId, "Benefit realisation", "%", KpiDirection.HigherIsBetter, target: 100m, amberThreshold: 80m);
    }
}
