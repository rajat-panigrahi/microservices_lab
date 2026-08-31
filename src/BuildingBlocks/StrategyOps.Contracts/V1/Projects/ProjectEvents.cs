namespace StrategyOps.Contracts.V1.Projects;

/// <summary>A project has been drafted. Nothing is committed to it yet.</summary>
public sealed record ProjectDraftCreated : IntegrationEvent
{
    public required Guid ProjectId { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required Guid ObjectiveId { get; init; }
    public required string Sponsor { get; init; }
    public required decimal Budget { get; init; }
}

/// <summary>
/// The trigger for the project initiation saga: KPI, Risk and Benefits each have work to do,
/// and the project is not Active until all three confirm.
/// </summary>
public sealed record ProjectInitiationRequested : IntegrationEvent
{
    public required Guid ProjectId { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required Guid ObjectiveId { get; init; }
    public required decimal Budget { get; init; }
}

/// <summary>Every downstream service provisioned successfully; the project is live.</summary>
public sealed record ProjectActivated : IntegrationEvent
{
    public required Guid ProjectId { get; init; }
    public required string Code { get; init; }
}

/// <summary>At least one downstream service refused or timed out, and compensation has run.</summary>
public sealed record ProjectInitiationFailed : IntegrationEvent
{
    public required Guid ProjectId { get; init; }
    public required string Code { get; init; }
    public required string Reason { get; init; }
}

/// <summary>RAG status moved. Carried as a string deliberately - see IntegrationEvent remarks.</summary>
public sealed record ProjectHealthChanged : IntegrationEvent
{
    public required Guid ProjectId { get; init; }
    public required string Code { get; init; }
    public required string Health { get; init; }
    public required string Reason { get; init; }
}

/// <summary>The project has finished. Downstream services wind their own records down.</summary>
public sealed record ProjectClosed : IntegrationEvent
{
    public required Guid ProjectId { get; init; }
    public required string Code { get; init; }
}
