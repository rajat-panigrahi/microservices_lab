using StrategyOps.BuildingBlocks.Domain;

namespace StrategyOps.Kpi.Api.Domain;

/// <summary>
/// A measure with a target and a tolerance, plus the direction that makes those meaningful.
/// </summary>
public sealed class KpiDefinition
{
    private KpiDefinition()
    {
    }

    public Guid Id { get; private set; }

    public Guid ScorecardId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Unit { get; private set; } = string.Empty;

    public KpiDirection Direction { get; private set; }

    public decimal Target { get; private set; }

    /// <summary>The tolerance boundary: still acceptable, but off target.</summary>
    public decimal AmberThreshold { get; private set; }

    public decimal? LatestValue { get; private set; }

    public DateTimeOffset? LatestPeriodEndUtc { get; private set; }

    public KpiRag Rag { get; private set; } = KpiRag.NotMeasured;

    public static KpiDefinition Create(
        Guid scorecardId,
        string name,
        string unit,
        KpiDirection direction,
        decimal target,
        decimal amberThreshold)
    {
        Guard.AgainstEmpty(scorecardId, "kpi.scorecard_required", "A KPI belongs to a scorecard.");

        // The amber line has to sit on the losing side of the target, or the bands overlap
        // and every reading is simultaneously Green and Red.
        Guard.Against(
            direction == KpiDirection.HigherIsBetter && amberThreshold > target,
            "kpi.amber_threshold_invalid",
            "For a higher-is-better KPI the amber threshold must be at or below the target.");

        Guard.Against(
            direction == KpiDirection.LowerIsBetter && amberThreshold < target,
            "kpi.amber_threshold_invalid",
            "For a lower-is-better KPI the amber threshold must be at or above the target.");

        return new KpiDefinition
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecardId,
            Name = Guard.AgainstBlank(name, "kpi.name_required", "A KPI needs a name."),
            Unit = Guard.AgainstBlank(unit, "kpi.unit_required", "A KPI needs a unit, e.g. % or days."),
            Direction = direction,
            Target = target,
            AmberThreshold = amberThreshold,
            Rag = KpiRag.NotMeasured
        };
    }

    /// <summary>
    /// Records a reading and returns the RAG status before it, so the caller can tell whether
    /// this measurement is a breach, a recovery, or neither - and publish accordingly.
    /// </summary>
    public KpiRag Record(decimal value, DateTimeOffset periodEndUtc)
    {
        var previous = Rag;

        LatestValue = value;
        LatestPeriodEndUtc = periodEndUtc;
        Rag = Evaluate(value);

        return previous;
    }

    public KpiRag Evaluate(decimal value) => Direction switch
    {
        KpiDirection.HigherIsBetter => value >= Target
            ? KpiRag.Green
            : value >= AmberThreshold
                ? KpiRag.Amber
                : KpiRag.Red,
        _ => value <= Target
            ? KpiRag.Green
            : value <= AmberThreshold
                ? KpiRag.Amber
                : KpiRag.Red
    };
}
