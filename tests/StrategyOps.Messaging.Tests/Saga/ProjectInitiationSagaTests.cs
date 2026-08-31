using MassTransit.Testing;
using Shouldly;
using StrategyOps.Contracts.V1.Benefits;
using StrategyOps.Contracts.V1.Kpis;
using StrategyOps.Contracts.V1.Projects;
using StrategyOps.Contracts.V1.Risks;
using StrategyOps.Contracts.V1.Sagas;

namespace StrategyOps.Messaging.Tests.Saga;

/// <summary>
/// The orchestrated distributed transaction: three services set a project up, and if any of
/// them refuses, the ones that succeeded are undone.
/// </summary>
public class ProjectInitiationSagaTests
{
    private static ProjectInitiationRequested AnInitiation(Guid projectId) => new()
    {
        ProjectId = projectId,
        Code = "PRJ-0007",
        Name = "Warehouse automation",
        ObjectiveId = Guid.NewGuid(),
        Budget = 250_000m,
        CorrelationId = "corr-saga"
    };

    private static KpiScorecardProvisioned KpiOk(Guid projectId) => new()
    {
        ProjectId = projectId, ScorecardId = Guid.NewGuid(), ProjectCode = "PRJ-0007", KpiCount = 3
    };

    private static RiskRegisterProvisioned RiskOk(Guid projectId) => new()
    {
        ProjectId = projectId, RegisterId = Guid.NewGuid(), ProjectCode = "PRJ-0007"
    };

    private static BenefitProfileRegistered BenefitOk(Guid projectId) => new()
    {
        ProjectId = projectId, ProfileId = Guid.NewGuid(), ProjectCode = "PRJ-0007", ForecastValue = 350_000m
    };

    [Fact]
    public async Task StartingInitiation_AsksAllThreeServicesInParallel()
    {
        await using var host = await SagaHost.StartAsync();
        var projectId = Guid.NewGuid();

        await host.PublishAsync(AnInitiation(projectId));

        (await host.Saga.Exists(projectId, x => x.Provisioning)).ShouldNotBeNull();

        (await host.Harness.Published.Any<ProvisionKpiScorecard>()).ShouldBeTrue();
        (await host.Harness.Published.Any<ProvisionRiskRegister>()).ShouldBeTrue();
        (await host.Harness.Published.Any<RegisterBenefitProfile>()).ShouldBeTrue();
    }

    [Fact]
    public async Task TheCommandsCarryTheCorrelationIdSoTheWholeFlowIsTraceable()
    {
        await using var host = await SagaHost.StartAsync();
        var projectId = Guid.NewGuid();

        await host.PublishAsync(AnInitiation(projectId));
        await host.Harness.Published.Any<ProvisionRiskRegister>();

        var command = host.Harness.Published.Select<ProvisionRiskRegister>().First().Context.Message;
        command.CorrelationId.ShouldBe("corr-saga");
        command.ProjectCode.ShouldBe("PRJ-0007");
    }

    [Fact]
    public async Task TwoOfThreeConfirmations_DoNotActivateTheProject()
    {
        await using var host = await SagaHost.StartAsync();
        var projectId = Guid.NewGuid();

        await host.PublishAsync(AnInitiation(projectId));
        await host.Saga.Exists(projectId, x => x.Provisioning);

        await host.PublishAsync(KpiOk(projectId));
        await host.PublishAsync(RiskOk(projectId));
        await Task.Delay(300);

        (await host.Harness.Published.Any<ActivateProject>()).ShouldBeFalse(
            "the project is not Active until every leg has confirmed");
    }

    [Fact]
    public async Task AllThreeConfirmations_ActivateTheProjectAndCompleteTheSaga()
    {
        await using var host = await SagaHost.StartAsync();
        var projectId = Guid.NewGuid();

        await host.PublishAsync(AnInitiation(projectId));
        await host.Saga.Exists(projectId, x => x.Provisioning);

        await host.PublishAsync(KpiOk(projectId));
        await host.PublishAsync(RiskOk(projectId));
        await host.PublishAsync(BenefitOk(projectId));

        (await host.Harness.Published.Any<ActivateProject>()).ShouldBeTrue();
        (await host.Saga.NotExists(projectId)).ShouldBeNull("a completed saga is finalized and removed");
    }

    [Fact]
    public async Task ConfirmationsArrivingInAnyOrder_StillActivate()
    {
        await using var host = await SagaHost.StartAsync();
        var projectId = Guid.NewGuid();

        await host.PublishAsync(AnInitiation(projectId));
        await host.Saga.Exists(projectId, x => x.Provisioning);

        // Deliberately the reverse of the order the commands went out. Nothing in a
        // distributed system guarantees replies come back in the order the requests left.
        await host.PublishAsync(BenefitOk(projectId));
        await host.PublishAsync(RiskOk(projectId));
        await host.PublishAsync(KpiOk(projectId));

        (await host.Harness.Published.Any<ActivateProject>()).ShouldBeTrue();
    }

    [Fact]
    public async Task ARefusedBenefitProfile_CompensatesTheTwoLegsThatSucceeded()
    {
        await using var host = await SagaHost.StartAsync();
        var projectId = Guid.NewGuid();

        await host.PublishAsync(AnInitiation(projectId));
        await host.Saga.Exists(projectId, x => x.Provisioning);

        await host.PublishAsync(KpiOk(projectId));
        await host.PublishAsync(RiskOk(projectId));

        await host.PublishAsync(new BenefitProfileRegistrationFailed
        {
            ProjectId = projectId,
            Reason = "Forecast benefit of 1,400,000 exceeds the portfolio ceiling of 1,000,000"
        });

        // Both successful legs are told to undo their work.
        (await host.Harness.Published.Any<WithdrawKpiScorecard>()).ShouldBeTrue();
        (await host.Harness.Published.Any<WithdrawRiskRegister>()).ShouldBeTrue();

        // Benefits created nothing, so it is not asked to withdraw anything.
        (await host.Harness.Published.Any<WithdrawBenefitProfile>()).ShouldBeFalse();

        // And the project is not failed until compensation has actually confirmed.
        (await host.Harness.Published.Any<FailProjectInitiation>()).ShouldBeFalse();
    }

    [Fact]
    public async Task TheProjectFailsOnlyAfterEveryCompensationHasConfirmed()
    {
        await using var host = await SagaHost.StartAsync();
        var projectId = Guid.NewGuid();

        await host.PublishAsync(AnInitiation(projectId));
        await host.Saga.Exists(projectId, x => x.Provisioning);
        await host.PublishAsync(KpiOk(projectId));
        await host.PublishAsync(RiskOk(projectId));
        await host.PublishAsync(new BenefitProfileRegistrationFailed { ProjectId = projectId, Reason = "over ceiling" });

        await host.Harness.Published.Any<WithdrawKpiScorecard>();

        await host.PublishAsync(new KpiScorecardWithdrawn { ProjectId = projectId });

        await Eventually.IsTrueAsync(
            async () => (await host.ReadStateAsync(projectId))?.KpiWithdrawn == true,
            "the KPI withdrawal is recorded");

        // Still compensating: the saga is waiting on Risk's confirmation, and until it comes
        // the project must not be told anything. Asserted on persisted state rather than on
        // "no message yet", which cannot distinguish "not sent" from "not sent yet".
        var midway = await host.ReadStateAsync(projectId);
        midway.ShouldNotBeNull();
        midway.CurrentState.ShouldBe("Compensating");
        midway.KpiWithdrawn.ShouldBeTrue();
        midway.RiskWithdrawn.ShouldBeFalse();

        await host.PublishAsync(new RiskRegisterWithdrawn { ProjectId = projectId });

        (await host.Harness.Published.Any<FailProjectInitiation>()).ShouldBeTrue();

        var failure = host.Harness.Published.Select<FailProjectInitiation>().First().Context.Message;
        failure.Reason.ShouldContain("Benefit profile");
        failure.Reason.ShouldContain("ceiling");

        await Eventually.IsTrueAsync(
            async () => await host.ReadStateAsync(projectId) is null,
            "the saga finalizes and removes its instance once compensation is done");
    }

    [Fact]
    public async Task AFailureBeforeAnyLegSucceeded_FailsImmediatelyWithNothingToUndo()
    {
        await using var host = await SagaHost.StartAsync();
        var projectId = Guid.NewGuid();

        await host.PublishAsync(AnInitiation(projectId));
        await host.Saga.Exists(projectId, x => x.Provisioning);

        await host.PublishAsync(new RiskRegisterProvisionFailed { ProjectId = projectId, Reason = "database unavailable" });

        (await host.Harness.Published.Any<FailProjectInitiation>()).ShouldBeTrue();
        (await host.Harness.Published.Any<WithdrawKpiScorecard>()).ShouldBeFalse();
        (await host.Harness.Published.Any<WithdrawRiskRegister>()).ShouldBeFalse();
    }

    [Fact]
    public async Task ALegConfirmingAfterCompensationBegan_IsWithdrawnImmediately()
    {
        await using var host = await SagaHost.StartAsync();
        var projectId = Guid.NewGuid();

        await host.PublishAsync(AnInitiation(projectId));
        await host.Saga.Exists(projectId, x => x.Provisioning);

        // Risk succeeds, Benefits refuses, and only THEN does the slow KPI service answer.
        await host.PublishAsync(RiskOk(projectId));
        await host.PublishAsync(new BenefitProfileRegistrationFailed { ProjectId = projectId, Reason = "over ceiling" });
        await host.Harness.Published.Any<WithdrawRiskRegister>();

        await host.PublishAsync(KpiOk(projectId));

        // Without this the scorecard survives, attached to a project that never activated -
        // the orphan that quietly accumulates in production for months.
        (await host.Harness.Published.Any<WithdrawKpiScorecard>()).ShouldBeTrue();
    }

    [Fact]
    public async Task TheSagaRemembersWhichLegsSucceeded_SoItCanCompensateAfterARestart()
    {
        await using var host = await SagaHost.StartAsync();
        var projectId = Guid.NewGuid();

        await host.PublishAsync(AnInitiation(projectId));
        await host.Saga.Exists(projectId, x => x.Provisioning);
        await host.PublishAsync(KpiOk(projectId));
        await Task.Delay(300);

        // State is persisted, not held in memory: this is what makes a saga survive a
        // process restart and still know there is a scorecard out there to withdraw.
        var instance = await host.ReadStateAsync(projectId);
        instance.ShouldNotBeNull();
        instance.KpiProvisioned.ShouldBeTrue();
        instance.RiskProvisioned.ShouldBeFalse();
        instance.ProjectCode.ShouldBe("PRJ-0007");
        instance.CurrentState.ShouldBe("Provisioning");
    }
}
