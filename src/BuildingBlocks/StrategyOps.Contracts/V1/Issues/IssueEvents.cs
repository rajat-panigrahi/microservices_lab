namespace StrategyOps.Contracts.V1.Issues;

public sealed record IssueRaised : IntegrationEvent
{
    public required Guid IssueId { get; init; }
    public required Guid ProjectId { get; init; }

    /// <summary>Set when the issue came from a risk materialising rather than being raised directly.</summary>
    public Guid? OriginRiskId { get; init; }

    public required string Title { get; init; }
    public required string Severity { get; init; }
}

public sealed record IssueAssigned : IntegrationEvent
{
    public required Guid IssueId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string Owner { get; init; }
}

public sealed record IssueResolved : IntegrationEvent
{
    public required Guid IssueId { get; init; }
    public required Guid ProjectId { get; init; }
    public Guid? OriginRiskId { get; init; }
}

public sealed record IssueBreachedSla : IntegrationEvent
{
    public required Guid IssueId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string Severity { get; init; }
    public required DateTimeOffset TargetResolutionUtc { get; init; }
}
