namespace StrategyOps.Risk.Api.Domain;

/// <summary>
/// Severity band derived from the probability x impact score. The bands are what drive
/// escalation policy, so they belong in the domain rather than in a report.
/// </summary>
public enum RiskTier
{
    Low,
    Medium,
    High,
    Critical
}

public enum RiskStatus
{
    /// <summary>Identified, no mitigation agreed yet.</summary>
    Open,

    /// <summary>A mitigation plan exists and is being worked.</summary>
    Mitigating,

    /// <summary>The risk happened. It is now an issue, and the Issues service owns it.</summary>
    Materialised,

    /// <summary>Retired without materialising.</summary>
    Closed
}

public enum RiskRegisterStatus
{
    Active,
    Closed
}
