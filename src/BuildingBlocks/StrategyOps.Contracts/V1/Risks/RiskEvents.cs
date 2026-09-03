namespace StrategyOps.Contracts.V1.Risks;

/// <summary>The Risk service has created a register for a project being initiated.</summary>
public sealed record RiskRegisterProvisioned : IntegrationEvent
{
    public required Guid ProjectId { get; init; }
    public required Guid RegisterId { get; init; }
    public required string ProjectCode { get; init; }
}

/// <summary>The Risk service could not provision a register. A saga leg has failed.</summary>
public sealed record RiskRegisterProvisionFailed : IntegrationEvent
{
    public required Guid ProjectId { get; init; }
    public required string Reason { get; init; }
}

/// <summary>Compensation completed: the register created during a failed initiation is gone.</summary>
public sealed record RiskRegisterWithdrawn : IntegrationEvent
{
    public required Guid ProjectId { get; init; }
}

public sealed record RiskRaised : IntegrationEvent
{
    public required Guid RiskId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string Title { get; init; }
    public required int Score { get; init; }
    public required string Tier { get; init; }
}

public sealed record RiskRescored : IntegrationEvent
{
    public required Guid RiskId { get; init; }
    public required Guid ProjectId { get; init; }
    public required int Score { get; init; }
    public required string Tier { get; init; }
}

/// <summary>
/// The risk has materialised. This is the first link in the choreographed chain:
/// Issues raises an issue, Projects drops RAG status, Benefits flags the benefit at risk -
/// none of them coordinated by anything.
/// </summary>
public sealed record RiskEscalated : IntegrationEvent
{
    public required Guid RiskId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string Title { get; init; }
    public required string Tier { get; init; }
    public required string Reason { get; init; }
}

public sealed record RiskClosed : IntegrationEvent
{
    public required Guid RiskId { get; init; }
    public required Guid ProjectId { get; init; }
}
