namespace StrategyOps.Contracts.V1.Kpis;

public sealed record KpiScorecardProvisioned : IntegrationEvent
{
    public required Guid ProjectId { get; init; }
    public required Guid ScorecardId { get; init; }
    public required string ProjectCode { get; init; }
    public required int KpiCount { get; init; }
}

public sealed record KpiScorecardProvisionFailed : IntegrationEvent
{
    public required Guid ProjectId { get; init; }
    public required string Reason { get; init; }
}

public sealed record KpiScorecardWithdrawn : IntegrationEvent
{
    public required Guid ProjectId { get; init; }
}

public sealed record KpiMeasurementRecorded : IntegrationEvent
{
    public required Guid KpiId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string KpiName { get; init; }
    public required decimal Value { get; init; }
    public required string Rag { get; init; }
}

/// <summary>A KPI has moved off Green. Benefits treats this as a signal that value is at risk.</summary>
public sealed record KpiBreached : IntegrationEvent
{
    public required Guid KpiId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string KpiName { get; init; }
    public required string Rag { get; init; }
    public required decimal Value { get; init; }
    public required decimal Target { get; init; }
}

public sealed record KpiRecovered : IntegrationEvent
{
    public required Guid KpiId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string KpiName { get; init; }
}
