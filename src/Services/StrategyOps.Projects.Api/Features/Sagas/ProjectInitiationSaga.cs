using MassTransit;
using StrategyOps.Contracts.V1.Benefits;
using StrategyOps.Contracts.V1.Kpis;
using StrategyOps.Contracts.V1.Projects;
using StrategyOps.Contracts.V1.Risks;
using StrategyOps.Contracts.V1.Sagas;

namespace StrategyOps.Projects.Api.Features.Sagas;

/// <summary>
/// Initiating a project has to touch four services at once. This is the orchestrator that
/// makes that safe.
/// </summary>
/// <remarks>
/// <para><b>The problem.</b> In a monolith, activating a project would open one database
/// transaction, create the KPI scorecard, the risk register and the benefit profile, set the
/// project Active, and commit. Across four services with four databases there is no such
/// transaction - and two-phase commit is not a trade worth making, because it holds locks
/// across the network and makes every participant's availability everyone's availability.</para>
///
/// <para><b>The answer.</b> A saga: a sequence of local transactions, each committing
/// independently, each with a compensating action. If any leg fails, the saga undoes the legs
/// that already succeeded. That is not a rollback - the work genuinely happened and is
/// genuinely being reversed, visibly, as business operations.</para>
///
/// <para><b>Orchestration, specifically.</b> This one file is the entire flow: what is asked
/// for, what counts as failure, what gets undone. That is the advantage - one place to read,
/// one place to debug, and a state column you can query to find every stuck project. The cost
/// is that this service now knows the names of three others, and adding a participant means
/// editing this file. The risk escalation chain in the same system is choreographed instead,
/// so the two can be compared side by side.</para>
///
/// <para><b>Three failure modes</b>, which is where hand-rolled sagas usually fall down:</para>
/// <list type="number">
///   <item>a leg reports failure - compensate the others;</item>
///   <item>a leg never answers - the scheduled timeout fires and compensates;</item>
///   <item>a leg answers <em>late</em>, after compensation began - the confirmation is caught
///   in the Compensating state and immediately withdrawn, so nothing is orphaned. This is the
///   one people forget, and it quietly leaks records for months.</item>
/// </list>
///
/// <para><b>A note on Publish vs Send.</b> The commands below are published rather than sent
/// to a named endpoint. MassTransit routes each type to the single consumer that subscribes
/// to it, so behaviour is identical here, and it keeps the lab free of endpoint address
/// configuration. In production you would map endpoint conventions and use Send, so that a
/// command with accidentally two consumers fails loudly instead of silently fanning out.</para>
/// </remarks>
public sealed class ProjectInitiationSaga : MassTransitStateMachine<ProjectInitiationState>
{
    public ProjectInitiationSaga()
    {
        InstanceState(x => x.CurrentState);

        // Every message in this flow carries ProjectId, and the saga instance is keyed on it.
        Event(() => InitiationRequested, x => x.CorrelateById(m => m.Message.ProjectId));
        Event(() => KpiProvisioned, x => x.CorrelateById(m => m.Message.ProjectId));
        Event(() => KpiFailed, x => x.CorrelateById(m => m.Message.ProjectId));
        Event(() => KpiWithdrawnConfirmed, x => x.CorrelateById(m => m.Message.ProjectId));
        Event(() => RiskProvisioned, x => x.CorrelateById(m => m.Message.ProjectId));
        Event(() => RiskFailed, x => x.CorrelateById(m => m.Message.ProjectId));
        Event(() => RiskWithdrawnConfirmed, x => x.CorrelateById(m => m.Message.ProjectId));
        Event(() => BenefitRegistered, x => x.CorrelateById(m => m.Message.ProjectId));
        Event(() => BenefitFailed, x => x.CorrelateById(m => m.Message.ProjectId));
        Event(() => BenefitWithdrawnConfirmed, x => x.CorrelateById(m => m.Message.ProjectId));

        Schedule(() => InitiationTimeout, x => x.TimeoutTokenId, x =>
        {
            // Long enough not to punish a slow service, short enough that a project cannot
            // sit in Initiating forever waiting for one that is never coming back. A saga
            // with no timeout is a saga that accumulates stuck instances.
            x.Delay = TimeSpan.FromSeconds(30);
            x.Received = e => e.CorrelateById(m => m.Message.ProjectId);
        });

        Initially(
            When(InitiationRequested)
                .Then(context =>
                {
                    context.Saga.ProjectCode = context.Message.Code;
                    context.Saga.StartedAtUtc = context.Message.OccurredAtUtc;
                })
                .Schedule(InitiationTimeout, context => context.Init<ProjectInitiationTimeout>(
                    new ProjectInitiationTimeout { ProjectId = context.Saga.CorrelationId }))

                // Three commands in parallel. The legs are independent, so serialising them
                // would only make initiation three times slower for no extra safety.
                .Publish(context => new ProvisionKpiScorecard
                {
                    ProjectId = context.Saga.CorrelationId,
                    ProjectCode = context.Message.Code,
                    ObjectiveId = context.Message.ObjectiveId,
                    CorrelationId = context.Message.CorrelationId
                })
                .Publish(context => new ProvisionRiskRegister
                {
                    ProjectId = context.Saga.CorrelationId,
                    ProjectCode = context.Message.Code,
                    CorrelationId = context.Message.CorrelationId
                })
                .Publish(context => new RegisterBenefitProfile
                {
                    ProjectId = context.Saga.CorrelationId,
                    ProjectCode = context.Message.Code,
                    ProjectName = context.Message.Name,
                    Budget = context.Message.Budget,
                    CorrelationId = context.Message.CorrelationId
                })
                .TransitionTo(Provisioning));

        During(Provisioning,
            When(KpiProvisioned).Then(c => c.Saga.KpiProvisioned = true).If(AllLegsDone, ActivateProject),
            When(RiskProvisioned).Then(c => c.Saga.RiskProvisioned = true).If(AllLegsDone, ActivateProject),
            When(BenefitRegistered).Then(c => c.Saga.BenefitRegistered = true).If(AllLegsDone, ActivateProject),

            When(KpiFailed)
                .Then(c =>
                {
                    c.Saga.FailureReason = $"KPI scorecard: {c.Message.Reason}";
                    c.Saga.KpiWithdrawn = true;
                })
                .TransitionTo(Compensating)
                .IfElse(NothingToCompensate, FailNow, StartCompensation),

            When(RiskFailed)
                .Then(c =>
                {
                    c.Saga.FailureReason = $"Risk register: {c.Message.Reason}";
                    c.Saga.RiskWithdrawn = true;
                })
                .TransitionTo(Compensating)
                .IfElse(NothingToCompensate, FailNow, StartCompensation),

            When(BenefitFailed)
                .Then(c =>
                {
                    c.Saga.FailureReason = $"Benefit profile: {c.Message.Reason}";
                    c.Saga.BenefitWithdrawn = true;
                })
                .TransitionTo(Compensating)
                .IfElse(NothingToCompensate, FailNow, StartCompensation),

            When(InitiationTimeout.Received)
                .Then(c => c.Saga.FailureReason = DescribeTimeout(c.Saga))
                .TransitionTo(Compensating)
                .IfElse(NothingToCompensate, FailNow, StartCompensation));

        During(Compensating,
            // A leg confirming after compensation began is withdrawn immediately. Without
            // this the scorecard survives a failed initiation, attached to a project that
            // never activated.
            When(KpiProvisioned).Then(c => c.Saga.KpiProvisioned = true).Publish(WithdrawKpi),
            When(RiskProvisioned).Then(c => c.Saga.RiskProvisioned = true).Publish(WithdrawRisk),
            When(BenefitRegistered).Then(c => c.Saga.BenefitRegistered = true).Publish(WithdrawBenefit),

            // A second leg failing while compensating just means one fewer thing to undo.
            When(KpiFailed).Then(c => c.Saga.KpiWithdrawn = true).If(CompensationDone, FailNow),
            When(RiskFailed).Then(c => c.Saga.RiskWithdrawn = true).If(CompensationDone, FailNow),
            When(BenefitFailed).Then(c => c.Saga.BenefitWithdrawn = true).If(CompensationDone, FailNow),

            When(KpiWithdrawnConfirmed).Then(c => c.Saga.KpiWithdrawn = true).If(CompensationDone, FailNow),
            When(RiskWithdrawnConfirmed).Then(c => c.Saga.RiskWithdrawn = true).If(CompensationDone, FailNow),
            When(BenefitWithdrawnConfirmed).Then(c => c.Saga.BenefitWithdrawn = true).If(CompensationDone, FailNow),

            // Compensation itself can hang. When it does, the project is still told the
            // initiation failed - leaving it in Initiating forever would be worse.
            When(InitiationTimeout.Received)
                .Then(c => c.Saga.FailureReason ??= "Initiation timed out")
                .Then(FailBinderless)
                .Finalize());

        SetCompletedWhenFinalized();
    }

    public State Provisioning { get; private set; } = null!;

    public State Compensating { get; private set; } = null!;

    public Event<ProjectInitiationRequested> InitiationRequested { get; private set; } = null!;

    public Event<KpiScorecardProvisioned> KpiProvisioned { get; private set; } = null!;

    public Event<KpiScorecardProvisionFailed> KpiFailed { get; private set; } = null!;

    public Event<KpiScorecardWithdrawn> KpiWithdrawnConfirmed { get; private set; } = null!;

    public Event<RiskRegisterProvisioned> RiskProvisioned { get; private set; } = null!;

    public Event<RiskRegisterProvisionFailed> RiskFailed { get; private set; } = null!;

    public Event<RiskRegisterWithdrawn> RiskWithdrawnConfirmed { get; private set; } = null!;

    public Event<BenefitProfileRegistered> BenefitRegistered { get; private set; } = null!;

    public Event<BenefitProfileRegistrationFailed> BenefitFailed { get; private set; } = null!;

    public Event<BenefitProfileWithdrawn> BenefitWithdrawnConfirmed { get; private set; } = null!;

    public Schedule<ProjectInitiationState, ProjectInitiationTimeout> InitiationTimeout { get; private set; } = null!;

    private static bool AllLegsDone<T>(BehaviorContext<ProjectInitiationState, T> context)
        where T : class => context.Saga.AllProvisioned;

    private static bool CompensationDone<T>(BehaviorContext<ProjectInitiationState, T> context)
        where T : class => context.Saga.CompensationComplete;

    private static bool NothingToCompensate<T>(BehaviorContext<ProjectInitiationState, T> context)
        where T : class => context.Saga.CompensationComplete;

    private static WithdrawKpiScorecard WithdrawKpi<T>(BehaviorContext<ProjectInitiationState, T> context)
        where T : class => new() { ProjectId = context.Saga.CorrelationId };

    private static WithdrawRiskRegister WithdrawRisk<T>(BehaviorContext<ProjectInitiationState, T> context)
        where T : class => new() { ProjectId = context.Saga.CorrelationId };

    private static WithdrawBenefitProfile WithdrawBenefit<T>(BehaviorContext<ProjectInitiationState, T> context)
        where T : class => new() { ProjectId = context.Saga.CorrelationId };

    /// <summary>All three legs confirmed: cancel the timeout, activate the project, finish.</summary>
    private EventActivityBinder<ProjectInitiationState, T> ActivateProject<T>(
        EventActivityBinder<ProjectInitiationState, T> binder)
        where T : class =>
        binder
            .Unschedule(InitiationTimeout)
            .Publish(context => new ActivateProject { ProjectId = context.Saga.CorrelationId })
            .Finalize();

    /// <summary>Send a withdrawal for every leg that actually succeeded.</summary>
    private EventActivityBinder<ProjectInitiationState, T> StartCompensation<T>(
        EventActivityBinder<ProjectInitiationState, T> binder)
        where T : class =>
        binder
            .Unschedule(InitiationTimeout)
            .If(c => c.Saga.KpiProvisioned && !c.Saga.KpiWithdrawn, x => x.Publish(WithdrawKpi))
            .If(c => c.Saga.RiskProvisioned && !c.Saga.RiskWithdrawn, x => x.Publish(WithdrawRisk))
            .If(c => c.Saga.BenefitRegistered && !c.Saga.BenefitWithdrawn, x => x.Publish(WithdrawBenefit));

    /// <summary>Nothing succeeded, or everything has been undone: report the failure and finish.</summary>
    private EventActivityBinder<ProjectInitiationState, T> FailNow<T>(
        EventActivityBinder<ProjectInitiationState, T> binder)
        where T : class =>
        binder
            .Unschedule(InitiationTimeout)
            .Publish(context => new FailProjectInitiation
            {
                ProjectId = context.Saga.CorrelationId,
                Reason = context.Saga.FailureReason ?? "Initiation failed"
            })
            .Finalize();

    private static void FailBinderless(BehaviorContext<ProjectInitiationState, ProjectInitiationTimeout> context) =>
        context.Publish(new FailProjectInitiation
        {
            ProjectId = context.Saga.CorrelationId,
            Reason = context.Saga.FailureReason ?? "Initiation failed"
        });

    private static string DescribeTimeout(ProjectInitiationState saga)
    {
        var missing = new List<string>(3);

        if (!saga.KpiProvisioned)
        {
            missing.Add("KPI");
        }

        if (!saga.RiskProvisioned)
        {
            missing.Add("Risk");
        }

        if (!saga.BenefitRegistered)
        {
            missing.Add("Benefits");
        }

        return $"Initiation timed out waiting for: {string.Join(", ", missing)}";
    }
}
