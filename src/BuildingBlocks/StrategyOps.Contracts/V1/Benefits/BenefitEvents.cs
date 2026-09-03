namespace StrategyOps.Contracts.V1.Benefits;

public sealed record BenefitProfileRegistered : IntegrationEvent
{
    public required Guid ProjectId { get; init; }
    public required Guid ProfileId { get; init; }
    public required string ProjectCode { get; init; }
    public required decimal ForecastValue { get; init; }
}

public sealed record BenefitProfileRegistrationFailed : IntegrationEvent
{
    public required Guid ProjectId { get; init; }
    public required string Reason { get; init; }
}

public sealed record BenefitProfileWithdrawn : IntegrationEvent
{
    public required Guid ProjectId { get; init; }
}

public sealed record BenefitRealised : IntegrationEvent
{
    public required Guid ProjectId { get; init; }
    public required Guid ProfileId { get; init; }
    public required decimal ActualValue { get; init; }
    public required decimal RealisedToDate { get; init; }
    public required decimal RealisationPercent { get; init; }
}

/// <summary>
/// The forecast value is in doubt - raised when a critical issue lands or a KPI breaches.
/// The fourth reaction in the choreographed chain.
/// </summary>
public sealed record BenefitAtRisk : IntegrationEvent
{
    public required Guid ProjectId { get; init; }
    public required Guid ProfileId { get; init; }
    public required string Reason { get; init; }
}
