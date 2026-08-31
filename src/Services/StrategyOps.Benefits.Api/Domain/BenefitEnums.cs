namespace StrategyOps.Benefits.Api.Domain;

/// <summary>
/// How a benefit is counted. The distinction is not academic - only cashable benefits can be
/// taken out of a budget, which is why finance cares which one a project is claiming.
/// </summary>
public enum BenefitType
{
    /// <summary>Money that actually leaves the cost base.</summary>
    Cashable,

    /// <summary>Time or capacity released, but not removed from the budget.</summary>
    NonCashable,

    /// <summary>Spend that would have happened and now will not.</summary>
    CostAvoidance
}

public enum BenefitStatus
{
    Registered,
    Realising,

    /// <summary>Forecast in doubt - a critical issue landed, or a KPI breached.</summary>
    AtRisk,

    Closed
}
