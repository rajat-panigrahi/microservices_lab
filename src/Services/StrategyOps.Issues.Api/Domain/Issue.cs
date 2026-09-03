using StrategyOps.BuildingBlocks.Domain;

namespace StrategyOps.Issues.Api.Domain;

/// <summary>
/// Something that has already gone wrong, as opposed to a risk, which might.
/// </summary>
/// <remarks>
/// Issues and Risks are separate services on purpose. They look similar enough that a first
/// pass usually merges them, but they answer to different people on different cadences - a
/// risk owner reviews a register monthly, an issue owner is chased against an SLA daily -
/// and they change for different reasons. That difference in <em>rate and reason for change</em>
/// is the real test for a service boundary, not how alike the tables look.
/// </remarks>
public sealed class Issue
{
    private Issue()
    {
    }

    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    /// <summary>Set when this issue was created by a risk materialising, null when raised directly.</summary>
    public Guid? OriginRiskId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public IssueSeverity Severity { get; private set; }

    public IssueStatus Status { get; private set; }

    public string? Owner { get; private set; }

    public string? ResolutionNotes { get; private set; }

    public DateTimeOffset RaisedAtUtc { get; private set; }

    public DateTimeOffset TargetResolutionUtc { get; private set; }

    public DateTimeOffset? ResolvedAtUtc { get; private set; }

    public static Issue Raise(Guid projectId, string title, IssueSeverity severity, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        ProjectId = Guard.AgainstEmpty(projectId, "issue.project_required", "An issue belongs to a project."),
        Title = Guard.AgainstBlank(title, "issue.title_required", "An issue needs a title."),
        Severity = severity,
        Status = IssueStatus.New,
        RaisedAtUtc = now,
        TargetResolutionUtc = now.AddDays(SlaDaysFor(severity))
    };

    /// <summary>
    /// Creates the issue that a materialised risk becomes. Called from the RiskEscalated
    /// consumer - this is the second link in the choreography chain.
    /// </summary>
    public static Issue RaiseFromRisk(Guid projectId, Guid riskId, string title, string riskTier, DateTimeOffset now)
    {
        var issue = Raise(projectId, title, SeverityFromRiskTier(riskTier), now);
        issue.OriginRiskId = Guard.AgainstEmpty(riskId, "issue.origin_risk_required", "An escalated issue must reference its risk.");
        return issue;
    }

    public void Assign(string owner)
    {
        RequireStatus(IssueStatus.New, IssueStatus.Assigned, IssueStatus.InProgress);

        Owner = Guard.AgainstBlank(owner, "issue.owner_required", "An issue needs a named owner.");
        if (Status == IssueStatus.New)
        {
            Status = IssueStatus.Assigned;
        }
    }

    public void Start()
    {
        RequireStatus(IssueStatus.Assigned);
        Status = IssueStatus.InProgress;
    }

    public void Resolve(string notes, DateTimeOffset now)
    {
        RequireStatus(IssueStatus.Assigned, IssueStatus.InProgress);

        ResolutionNotes = Guard.AgainstBlank(notes, "issue.resolution_required", "Resolving an issue needs resolution notes.");
        Status = IssueStatus.Resolved;
        ResolvedAtUtc = now;
    }

    public void Close()
    {
        RequireStatus(IssueStatus.Resolved);
        Status = IssueStatus.Closed;
    }

    /// <summary>An open issue past its target date. Resolved issues never breach retroactively.</summary>
    public bool HasBreachedSla(DateTimeOffset now) =>
        Status is not (IssueStatus.Resolved or IssueStatus.Closed) && now > TargetResolutionUtc;

    /// <summary>
    /// Risk tier arrives as a string, not a shared enum, so this has to cope with a value it
    /// does not recognise - including one added by a newer version of the Risk service.
    /// Defaulting to Medium keeps an unknown tier visible rather than dropping it.
    /// </summary>
    public static IssueSeverity SeverityFromRiskTier(string riskTier) => riskTier switch
    {
        "Critical" => IssueSeverity.Critical,
        "High" => IssueSeverity.High,
        _ => IssueSeverity.Medium
    };

    private static int SlaDaysFor(IssueSeverity severity) => severity switch
    {
        IssueSeverity.Critical => 2,
        IssueSeverity.High => 5,
        IssueSeverity.Medium => 10,
        _ => 20
    };

    private void RequireStatus(params IssueStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new DomainException(
                "issue.invalid_status_transition",
                $"An issue with status '{Status}' cannot do this; expected one of: {string.Join(", ", allowed)}.");
        }
    }
}
