namespace StrategyOps.Contracts.V1.Sagas;

/// <summary>
/// Commands issued by the project initiation saga.
/// </summary>
/// <remarks>
/// <para>
/// The distinction between these and the events in the sibling folders is the whole
/// difference between orchestration and choreography, and it is worth being precise about:
/// </para>
/// <list type="bullet">
///   <item>
///     An <b>event</b> states a fact that has already happened, in the past tense
///     (<c>RiskEscalated</c>). It is <b>published</b>, it may have zero or many subscribers,
///     and the publisher neither knows nor cares who reacts.
///   </item>
///   <item>
///     A <b>command</b> asks one specific service to do one specific thing, in the
///     imperative (<c>ProvisionRiskRegister</c>). It is <b>sent</b> to one endpoint, it has
///     exactly one handler, and the sender is waiting to hear how it went.
///   </item>
/// </list>
/// <para>
/// Because the saga sends commands, one file - ProjectInitiationSaga - describes the entire
/// initiation flow, including what to undo when it fails. Compare the risk escalation chain,
/// which is choreographed: no file describes it, and you find it by searching for consumers.
/// </para>
/// </remarks>
public abstract record SagaCommand
{
    public Guid MessageId { get; init; } = Guid.NewGuid();

    public required Guid ProjectId { get; init; }

    public string CorrelationId { get; init; } = string.Empty;
}

// ---------------------------------------------------------------------------
// Forward path: three services are asked to set a project up.
// ---------------------------------------------------------------------------

public sealed record ProvisionKpiScorecard : SagaCommand
{
    public required string ProjectCode { get; init; }
    public required Guid ObjectiveId { get; init; }
}

public sealed record ProvisionRiskRegister : SagaCommand
{
    public required string ProjectCode { get; init; }
}

public sealed record RegisterBenefitProfile : SagaCommand
{
    public required string ProjectCode { get; init; }
    public required string ProjectName { get; init; }
    public required decimal Budget { get; init; }
}

// ---------------------------------------------------------------------------
// Compensation: undo the legs that succeeded, because another one did not.
// ---------------------------------------------------------------------------

public sealed record WithdrawKpiScorecard : SagaCommand;

public sealed record WithdrawRiskRegister : SagaCommand;

public sealed record WithdrawBenefitProfile : SagaCommand;

// ---------------------------------------------------------------------------
// Outcome, sent back to the Projects service to move the aggregate.
// ---------------------------------------------------------------------------

public sealed record ActivateProject : SagaCommand;

public sealed record FailProjectInitiation : SagaCommand
{
    public required string Reason { get; init; }
}
