namespace StrategyOps.Kpi.Api.Domain;

/// <summary>
/// Whether a bigger number is better. Without this, thresholds are ambiguous: is a
/// cost-per-order KPI of 4.20 good or bad?
/// </summary>
public enum KpiDirection
{
    HigherIsBetter,
    LowerIsBetter
}

public enum KpiRag
{
    Green,
    Amber,
    Red,

    /// <summary>No measurement recorded yet - deliberately distinct from Red.</summary>
    NotMeasured
}

public enum ScorecardStatus
{
    Active,
    Closed
}
