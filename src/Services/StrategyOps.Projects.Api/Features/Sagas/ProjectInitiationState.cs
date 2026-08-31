using MassTransit;

namespace StrategyOps.Projects.Api.Features.Sagas;

/// <summary>
/// The saga's memory: what has been asked for, what has come back, and what still has to be
/// undone if this goes wrong.
/// </summary>
/// <remarks>
/// This is persisted in the Projects database, which is what makes the saga survive a
/// restart. An in-memory coordinator that forgets which legs succeeded is worse than no
/// coordinator at all - it cannot compensate.
/// </remarks>
public sealed class ProjectInitiationState : SagaStateMachineInstance, ISagaVersion
{
    /// <summary>The project id. Every message in the flow carries it, which is how they correlate.</summary>
    public Guid CorrelationId { get; set; }

    /// <summary>Optimistic concurrency: two confirmations arriving at once must not overwrite each other.</summary>
    public int Version { get; set; }

    public string CurrentState { get; set; } = string.Empty;

    public string ProjectCode { get; set; } = string.Empty;

    public DateTimeOffset StartedAtUtc { get; set; }

    // Forward path: which legs have confirmed.
    public bool KpiProvisioned { get; set; }

    public bool RiskProvisioned { get; set; }

    public bool BenefitRegistered { get; set; }

    // Compensation: which of those have been undone again.
    public bool KpiWithdrawn { get; set; }

    public bool RiskWithdrawn { get; set; }

    public bool BenefitWithdrawn { get; set; }

    public string? FailureReason { get; set; }

    /// <summary>Token for the scheduled timeout, so it can be cancelled once the saga completes.</summary>
    public Guid? TimeoutTokenId { get; set; }

    public bool AllProvisioned => KpiProvisioned && RiskProvisioned && BenefitRegistered;

    /// <summary>
    /// Compensation is finished when every leg that actually succeeded has been undone.
    /// A leg that never provisioned needs no withdrawal - which is why these are paired.
    /// </summary>
    public bool CompensationComplete =>
        (!KpiProvisioned || KpiWithdrawn)
        && (!RiskProvisioned || RiskWithdrawn)
        && (!BenefitRegistered || BenefitWithdrawn);
}

/// <summary>Scheduled when initiation starts; delivered if the saga is still waiting.</summary>
public sealed record ProjectInitiationTimeout
{
    public required Guid ProjectId { get; init; }
}
