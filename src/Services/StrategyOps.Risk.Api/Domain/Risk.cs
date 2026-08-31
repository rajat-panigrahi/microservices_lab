using StrategyOps.BuildingBlocks.Domain;

namespace StrategyOps.Risk.Api.Domain;

/// <summary>
/// One entry on a project's risk register, scored on the standard 5x5 probability/impact
/// matrix.
/// </summary>
public sealed class Risk
{
    public const int MinScale = 1;
    public const int MaxScale = 5;

    private Risk()
    {
    }

    public Guid Id { get; private set; }

    public Guid RegisterId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Category { get; private set; } = string.Empty;

    public int Probability { get; private set; }

    public int Impact { get; private set; }

    /// <summary>Probability x impact, 1 to 25. Stored rather than computed so it is queryable.</summary>
    public int Score { get; private set; }

    public RiskTier Tier { get; private set; }

    public RiskStatus Status { get; private set; }

    public string Owner { get; private set; } = string.Empty;

    public string? MitigationPlan { get; private set; }

    public string? EscalationReason { get; private set; }

    public string? Resolution { get; private set; }

    public DateTimeOffset RaisedAtUtc { get; private set; }

    public DateTimeOffset? EscalatedAtUtc { get; private set; }

    public static Risk Raise(
        Guid registerId,
        string title,
        string category,
        int probability,
        int impact,
        string owner,
        DateTimeOffset now)
    {
        var risk = new Risk
        {
            Id = Guid.NewGuid(),
            RegisterId = Guard.AgainstEmpty(registerId, "risk.register_required", "A risk must belong to a project's risk register."),
            Title = Guard.AgainstBlank(title, "risk.title_required", "A risk needs a title."),
            Category = Guard.AgainstBlank(category, "risk.category_required", "A risk needs a category, e.g. Supplier or Technical."),
            Owner = Guard.AgainstBlank(owner, "risk.owner_required", "A risk needs a named owner."),
            Status = RiskStatus.Open,
            RaisedAtUtc = now
        };

        risk.ApplyScore(probability, impact);
        return risk;
    }

    public void Rescore(int probability, int impact)
    {
        RequireStatus(RiskStatus.Open, RiskStatus.Mitigating);
        ApplyScore(probability, impact);
    }

    public void PlanMitigation(string plan)
    {
        RequireStatus(RiskStatus.Open, RiskStatus.Mitigating);

        MitigationPlan = Guard.AgainstBlank(plan, "risk.mitigation_required", "A mitigation plan cannot be empty.");
        Status = RiskStatus.Mitigating;
    }

    /// <summary>
    /// The risk has happened.
    /// </summary>
    /// <remarks>
    /// This is the trigger for the choreographed chain: the Issues service raises an issue,
    /// Projects drops the RAG status, and Benefits flags the benefit at risk - with no
    /// coordinator. Escalating twice is rejected precisely so that chain can only start once,
    /// even if the request is retried.
    /// </remarks>
    public void Escalate(string reason, DateTimeOffset now)
    {
        RequireStatus(RiskStatus.Open, RiskStatus.Mitigating);

        EscalationReason = Guard.AgainstBlank(reason, "risk.escalation_reason_required", "Escalating a risk needs a reason.");
        Status = RiskStatus.Materialised;
        EscalatedAtUtc = now;
    }

    public void Close(string resolution)
    {
        RequireStatus(RiskStatus.Open, RiskStatus.Mitigating, RiskStatus.Materialised);

        Resolution = Guard.AgainstBlank(resolution, "risk.resolution_required", "Closing a risk needs a resolution note.");
        Status = RiskStatus.Closed;
    }

    private void ApplyScore(int probability, int impact)
    {
        Guard.Against(
            probability is < MinScale or > MaxScale,
            "risk.probability_out_of_range",
            $"Probability must be between {MinScale} and {MaxScale}.");

        Guard.Against(
            impact is < MinScale or > MaxScale,
            "risk.impact_out_of_range",
            $"Impact must be between {MinScale} and {MaxScale}.");

        Probability = probability;
        Impact = impact;
        Score = probability * impact;
        Tier = TierFor(Score);
    }

    /// <summary>The standard PMO banding of a 1-25 score.</summary>
    public static RiskTier TierFor(int score) => score switch
    {
        <= 4 => RiskTier.Low,
        <= 9 => RiskTier.Medium,
        <= 15 => RiskTier.High,
        _ => RiskTier.Critical
    };

    private void RequireStatus(params RiskStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new DomainException(
                "risk.invalid_status_transition",
                $"A risk with status '{Status}' cannot do this; expected one of: {string.Join(", ", allowed)}.");
        }
    }
}
