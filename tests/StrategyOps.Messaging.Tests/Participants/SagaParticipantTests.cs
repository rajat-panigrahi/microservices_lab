using MassTransit;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using StrategyOps.Benefits.Api.Domain;
using StrategyOps.Benefits.Api.Features.Consumers;
using StrategyOps.Benefits.Api.Infrastructure;
using StrategyOps.Contracts.V1.Benefits;
using StrategyOps.Contracts.V1.Issues;
using StrategyOps.Contracts.V1.Kpis;
using StrategyOps.Contracts.V1.Sagas;
using StrategyOps.Kpi.Api.Domain;
using StrategyOps.Kpi.Api.Features.Consumers;
using StrategyOps.Kpi.Api.Infrastructure;

namespace StrategyOps.Messaging.Tests.Participants;

/// <summary>
/// The KPI and Benefits legs of the initiation saga, tested from the participant's side:
/// does it do the work, and does it always answer?
/// </summary>
public class SagaParticipantTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);

    // ---------------------------------------------------------------- KPI

    [Fact]
    public async Task ProvisioningAScorecard_SeedsTheBaselineKpisAndConfirms()
    {
        await using var host = await ConsumerHost<KpiDbContext>.StartAsync(
            bus => bus.AddConsumer<ProvisionKpiScorecardConsumer>(), Now);

        var projectId = Guid.NewGuid();

        await host.PublishAsync(
            new ProvisionKpiScorecard
            {
                ProjectId = projectId,
                ProjectCode = "PRJ-0007",
                ObjectiveId = Guid.NewGuid(),
                CorrelationId = "corr-saga"
            },
            Guid.NewGuid());

        await host.Harness.Consumed.Any<ProvisionKpiScorecard>();

        var scorecard = await host.QueryAsync(db => db.Scorecards.SingleOrDefaultAsync(s => s.ProjectId == projectId));
        scorecard.ShouldNotBeNull();

        var kpis = await host.QueryAsync(db => db.Kpis.CountAsync(k => k.ScorecardId == scorecard.Id));
        kpis.ShouldBe(3, "an empty scorecard would make provisioning a no-op with nothing to compensate");

        var confirmations = await host.QueryAsync(db => db.OutboxMessages
            .CountAsync(m => m.Type == typeof(KpiScorecardProvisioned).FullName));
        confirmations.ShouldBe(1);
    }

    [Fact]
    public async Task WithdrawingAScorecard_RemovesItsKpisAndConfirms()
    {
        await using var host = await ConsumerHost<KpiDbContext>.StartAsync(
            bus =>
            {
                bus.AddConsumer<ProvisionKpiScorecardConsumer>();
                bus.AddConsumer<WithdrawKpiScorecardConsumer>();
            },
            Now);

        var projectId = Guid.NewGuid();
        await host.PublishAsync(
            new ProvisionKpiScorecard { ProjectId = projectId, ProjectCode = "PRJ-0007", ObjectiveId = Guid.NewGuid() },
            Guid.NewGuid());
        await host.Harness.Consumed.Any<ProvisionKpiScorecard>();

        await host.PublishAsync(new WithdrawKpiScorecard { ProjectId = projectId }, Guid.NewGuid());
        await host.Harness.Consumed.Any<WithdrawKpiScorecard>();

        (await host.QueryAsync(db => db.Scorecards.CountAsync(s => s.ProjectId == projectId))).ShouldBe(0);
        (await host.QueryAsync(db => db.Kpis.CountAsync())).ShouldBe(0);

        var confirmations = await host.QueryAsync(db => db.OutboxMessages
            .CountAsync(m => m.Type == typeof(KpiScorecardWithdrawn).FullName));
        confirmations.ShouldBe(1);
    }

    [Fact]
    public async Task WithdrawingAScorecardThatWasNeverCreated_StillConfirms()
    {
        await using var host = await ConsumerHost<KpiDbContext>.StartAsync(
            bus => bus.AddConsumer<WithdrawKpiScorecardConsumer>(), Now);

        await host.PublishAsync(new WithdrawKpiScorecard { ProjectId = Guid.NewGuid() }, Guid.NewGuid());
        await host.Harness.Consumed.Any<WithdrawKpiScorecard>();

        var confirmations = await host.QueryAsync(db => db.OutboxMessages
            .CountAsync(m => m.Type == typeof(KpiScorecardWithdrawn).FullName));

        confirmations.ShouldBe(1, "silence here would hang the saga until its timeout");
    }

    // ------------------------------------------------------------ Benefits

    [Fact]
    public async Task AForecastWithinTheCeiling_IsRegisteredAndConfirmed()
    {
        await using var host = await ConsumerHost<BenefitsDbContext>.StartAsync(
            bus => bus.AddConsumer<RegisterBenefitProfileConsumer>(), Now);

        var projectId = Guid.NewGuid();

        await host.PublishAsync(
            new RegisterBenefitProfile
            {
                ProjectId = projectId,
                ProjectCode = "PRJ-0007",
                ProjectName = "Warehouse automation",
                Budget = 250_000m
            },
            Guid.NewGuid());

        await host.Harness.Consumed.Any<RegisterBenefitProfile>();

        var profile = await host.QueryAsync(db => db.Profiles.SingleOrDefaultAsync(p => p.ProjectId == projectId));
        profile.ShouldNotBeNull();
        profile.ForecastValue.ShouldBe(350_000m, "budget 250,000 x the 1.4 multiplier");

        var confirmations = await host.QueryAsync(db => db.OutboxMessages
            .CountAsync(m => m.Type == typeof(BenefitProfileRegistered).FullName));
        confirmations.ShouldBe(1);
    }

    [Fact]
    public async Task AForecastOverTheCeiling_IsRefusedAsAnEventRatherThanAnException()
    {
        await using var host = await ConsumerHost<BenefitsDbContext>.StartAsync(
            bus => bus.AddConsumer<RegisterBenefitProfileConsumer>(), Now);

        var projectId = Guid.NewGuid();

        // 900,000 x 1.4 = 1,260,000, over the 1,000,000 default ceiling.
        await host.PublishAsync(
            new RegisterBenefitProfile
            {
                ProjectId = projectId,
                ProjectCode = "PRJ-0099",
                ProjectName = "Group-wide transformation",
                Budget = 900_000m
            },
            Guid.NewGuid());

        await host.Harness.Consumed.Any<RegisterBenefitProfile>();

        (await host.QueryAsync(db => db.Profiles.CountAsync(p => p.ProjectId == projectId))).ShouldBe(0);

        var refusals = await host.QueryAsync(db => db.OutboxMessages
            .CountAsync(m => m.Type == typeof(BenefitProfileRegistrationFailed).FullName));

        // Thrown instead, this would be retried five times and dead-lettered, and the saga
        // would wait for its timeout rather than compensating promptly.
        refusals.ShouldBe(1);

        var faulted = await host.Harness.Consumed.Any<RegisterBenefitProfile>(x => x.Context.ReceiveContext.IsFaulted);
        faulted.ShouldBeFalse("a business refusal is not a transient fault");
    }

    [Fact]
    public async Task ACriticalIssue_FlagsTheBenefitAtRiskExactlyOnce()
    {
        await using var host = await ConsumerHost<BenefitsDbContext>.StartAsync(
            bus => bus.AddConsumer<FlagBenefitAtRiskOnIssueRaisedConsumer>(), Now);

        var projectId = Guid.NewGuid();
        await host.SeedAsync(async db =>
        {
            db.Profiles.Add(BenefitProfile.Register(projectId, "PRJ-0007", "Warehouse savings", BenefitType.Cashable, 350_000m, Now));
            await Task.CompletedTask;
        });

        await host.PublishAsync(
            new IssueRaised { IssueId = Guid.NewGuid(), ProjectId = projectId, Title = "Supplier missed the date", Severity = "Critical" },
            Guid.NewGuid());
        await host.Harness.Consumed.Any<IssueRaised>();

        await host.PublishAsync(
            new IssueRaised { IssueId = Guid.NewGuid(), ProjectId = projectId, Title = "Another critical problem", Severity = "Critical" },
            Guid.NewGuid());
        await Task.Delay(300);

        var profile = await host.QueryAsync(db => db.Profiles.SingleAsync(p => p.ProjectId == projectId));
        profile.Status.ShouldBe(BenefitStatus.AtRisk);

        var flags = await host.QueryAsync(db => db.OutboxMessages.CountAsync(m => m.Type == typeof(BenefitAtRisk).FullName));
        flags.ShouldBe(1, "already at risk; re-announcing it would make the signal meaningless");
    }

    [Fact]
    public async Task ANonCriticalIssue_DoesNotFlagTheBenefit()
    {
        await using var host = await ConsumerHost<BenefitsDbContext>.StartAsync(
            bus => bus.AddConsumer<FlagBenefitAtRiskOnIssueRaisedConsumer>(), Now);

        var projectId = Guid.NewGuid();
        await host.SeedAsync(async db =>
        {
            db.Profiles.Add(BenefitProfile.Register(projectId, "PRJ-0007", "Warehouse savings", BenefitType.Cashable, 350_000m, Now));
            await Task.CompletedTask;
        });

        await host.PublishAsync(
            new IssueRaised { IssueId = Guid.NewGuid(), ProjectId = projectId, Title = "Minor data problem", Severity = "Medium" },
            Guid.NewGuid());
        await host.Harness.Consumed.Any<IssueRaised>();

        var profile = await host.QueryAsync(db => db.Profiles.SingleAsync(p => p.ProjectId == projectId));
        profile.Status.ShouldBe(BenefitStatus.Registered);
    }

    [Fact]
    public async Task ARedKpi_FlagsTheBenefitAtRisk()
    {
        await using var host = await ConsumerHost<BenefitsDbContext>.StartAsync(
            bus => bus.AddConsumer<FlagBenefitAtRiskOnKpiBreachedConsumer>(), Now);

        var projectId = Guid.NewGuid();
        await host.SeedAsync(async db =>
        {
            db.Profiles.Add(BenefitProfile.Register(projectId, "PRJ-0007", "Warehouse savings", BenefitType.Cashable, 350_000m, Now));
            await Task.CompletedTask;
        });

        await host.PublishAsync(
            new KpiBreached
            {
                KpiId = Guid.NewGuid(),
                ProjectId = projectId,
                KpiName = "Benefit realisation",
                Rag = "Red",
                Value = 40m,
                Target = 100m
            },
            Guid.NewGuid());

        await host.Harness.Consumed.Any<KpiBreached>();

        var profile = await host.QueryAsync(db => db.Profiles.SingleAsync(p => p.ProjectId == projectId));
        profile.Status.ShouldBe(BenefitStatus.AtRisk);
        profile.AtRiskReason.ShouldNotBeNull().ShouldContain("Benefit realisation");
    }
}
