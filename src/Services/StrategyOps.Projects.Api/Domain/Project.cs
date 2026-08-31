using StrategyOps.BuildingBlocks.Domain;

namespace StrategyOps.Projects.Api.Domain;

/// <summary>
/// The portfolio's unit of delivery, and the aggregate that owns the stage machine.
/// </summary>
/// <remarks>
/// Every stage transition is a method on this class, and every illegal one throws. Handlers
/// therefore cannot leave a project in a state the business does not recognise, no matter
/// what arrives over HTTP or off the bus - which matters more here than in a monolith,
/// because messages get redelivered and arrive out of order.
/// </remarks>
public sealed class Project
{
    private Project()
    {
    }

    public Guid Id { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public Guid ObjectiveId { get; private set; }

    public string Sponsor { get; private set; } = string.Empty;

    public decimal Budget { get; private set; }

    public ProjectStage Stage { get; private set; }

    public ProjectHealth Health { get; private set; }

    public string? HealthReason { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? ActivatedAtUtc { get; private set; }

    public DateTimeOffset? ClosedAtUtc { get; private set; }

    public static Project CreateDraft(
        string code,
        string name,
        Guid objectiveId,
        string sponsor,
        decimal budget,
        DateTimeOffset now) => new()
        {
            Id = Guid.NewGuid(),
            Code = Guard.AgainstBlank(code, "project.code_required", "A project needs a code.").ToUpperInvariant(),
            Name = Guard.AgainstBlank(name, "project.name_required", "A project needs a name."),
            ObjectiveId = Guard.AgainstEmpty(objectiveId, "project.objective_required", "A project must deliver against a strategic objective."),
            Sponsor = Guard.AgainstBlank(sponsor, "project.sponsor_required", "A project needs an executive sponsor."),
            Budget = Guard.AgainstNonPositive(budget, "project.budget_must_be_positive", "A project needs a budget greater than zero."),
            Stage = ProjectStage.Draft,
            Health = ProjectHealth.Green,
            CreatedAtUtc = now
        };

    /// <summary>
    /// Hands the project to the initiation saga. Allowed from Draft, and from
    /// InitiationFailed so a transient downstream outage can simply be retried.
    /// </summary>
    public void SubmitForInitiation(DateTimeOffset now)
    {
        RequireStage(ProjectStage.Draft, ProjectStage.InitiationFailed);

        Stage = ProjectStage.Initiating;
        FailureReason = null;
        _ = now;
    }

    /// <summary>All three downstream services confirmed; the project is live.</summary>
    public void CompleteInitiation(DateTimeOffset now)
    {
        RequireStage(ProjectStage.Initiating);

        Stage = ProjectStage.Active;
        ActivatedAtUtc = now;
        FailureReason = null;
    }

    /// <summary>A downstream service refused or timed out and compensation has run.</summary>
    public void FailInitiation(string reason)
    {
        RequireStage(ProjectStage.Initiating);

        Stage = ProjectStage.InitiationFailed;
        FailureReason = Guard.AgainstBlank(reason, "project.failure_reason_required", "Recording a failed initiation needs a reason.");
    }

    /// <summary>
    /// Moves RAG status. Returns whether anything actually changed, so callers only publish
    /// a ProjectHealthChanged event on a real transition - the same escalation arriving twice
    /// off the bus must not produce two events.
    /// </summary>
    public bool SetHealth(ProjectHealth health, string reason)
    {
        Guard.Against(Stage == ProjectStage.Closed, "project.closed", "A closed project's health cannot be changed.");

        if (Health == health)
        {
            return false;
        }

        Health = health;
        HealthReason = Guard.AgainstBlank(reason, "project.health_reason_required", "Changing project health needs a reason.");
        return true;
    }

    public void PutOnHold(string reason)
    {
        RequireStage(ProjectStage.Active);

        Stage = ProjectStage.OnHold;
        HealthReason = Guard.AgainstBlank(reason, "project.hold_reason_required", "Putting a project on hold needs a reason.");
    }

    public void Resume()
    {
        RequireStage(ProjectStage.OnHold);

        Stage = ProjectStage.Active;
    }

    public void Close(DateTimeOffset now)
    {
        RequireStage(ProjectStage.Active, ProjectStage.OnHold);

        Stage = ProjectStage.Closed;
        ClosedAtUtc = now;
    }

    private void RequireStage(params ProjectStage[] allowed)
    {
        if (!allowed.Contains(Stage))
        {
            throw new DomainException(
                "project.invalid_stage_transition",
                $"A project in stage '{Stage}' cannot do this; expected one of: {string.Join(", ", allowed)}.");
        }
    }
}
