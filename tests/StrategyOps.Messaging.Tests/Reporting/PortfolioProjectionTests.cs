using MassTransit;
using Shouldly;
using StrategyOps.Contracts.V1.Benefits;
using StrategyOps.Contracts.V1.Issues;
using StrategyOps.Contracts.V1.Kpis;
using StrategyOps.Contracts.V1.Projects;
using StrategyOps.Contracts.V1.Risks;
using StrategyOps.Reporting.Api.Features.Projections;

namespace StrategyOps.Messaging.Tests.Reporting;

/// <summary>
/// The CQRS read side: one flat row assembled from five services that never share a database.
/// </summary>
public class PortfolioProjectionTests
{
    private static readonly Guid ProjectId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    private static ProjectDraftCreated ADraft(Guid projectId) => new()
    {
        ProjectId = projectId,
        Code = "PRJ-0007",
        Name = "Warehouse automation",
        ObjectiveId = Guid.NewGuid(),
        Sponsor = "A. Sponsor",
        Budget = 250_000m
    };

    [Fact]
    public async Task ADraftProject_AppearsOnTheDashboard()
    {
        await using var host = await ReportingHost.StartAsync(bus => bus.AddConsumer<ProjectDraftCreatedProjection>());

        await host.PublishAsync(ADraft(ProjectId));
        await host.Harness.Consumed.Any<ProjectDraftCreated>();

        var row = await host.ReadAsync(ProjectId);
        row.ShouldNotBeNull();
        row.ProjectCode.ShouldBe("PRJ-0007");
        row.Budget.ShouldBe(250_000m);
        row.OverallStatus.ShouldBe("Green");
    }

    [Fact]
    public async Task EveryProjectionPushesToTheDashboard()
    {
        await using var host = await ReportingHost.StartAsync(bus => bus.AddConsumer<ProjectDraftCreatedProjection>());

        await host.PublishAsync(ADraft(ProjectId));
        await host.Harness.Consumed.Any<ProjectDraftCreated>();

        await Eventually.IsTrueAsync(
            () => Task.FromResult(host.Notifier.Pushes.Any(p => p.ProjectId == ProjectId)),
            "the dashboard was told about the change");
    }

    [Fact]
    public async Task AnEventForAProjectThisServiceHasNotSeenYet_CreatesTheRowAnyway()
    {
        await using var host = await ReportingHost.StartAsync(bus => bus.AddConsumer<RiskRaisedProjection>());

        // No ProjectDraftCreated first. Across five independent services, events genuinely do
        // arrive before the one that "created" the thing they refer to - so projections upsert
        // rather than insert, and ordering stops mattering.
        await host.PublishAsync(new RiskRaised
        {
            RiskId = Guid.NewGuid(), ProjectId = ProjectId, Title = "Supplier risk", Score = 25, Tier = "Critical"
        });
        await host.Harness.Consumed.Any<RiskRaised>();

        var row = await host.ReadAsync(ProjectId);
        row.ShouldNotBeNull();
        row.OpenRisks.ShouldBe(1);
        row.CriticalOpenRisks.ShouldBe(1);
    }

    [Fact]
    public async Task ARedeliveredEvent_DoesNotDoubleCount()
    {
        await using var host = await ReportingHost.StartAsync(bus => bus.AddConsumer<RiskRaisedProjection>());

        var messageId = Guid.NewGuid();
        var raised = new RiskRaised
        {
            RiskId = Guid.NewGuid(), ProjectId = ProjectId, Title = "Supplier risk", Score = 25, Tier = "Critical"
        };

        await host.PublishAsync(raised, messageId);
        await host.Harness.Consumed.Any<RiskRaised>();
        await host.PublishAsync(raised, messageId);
        await Task.Delay(200);

        var row = await host.ReadAsync(ProjectId);
        row!.OpenRisks.ShouldBe(1, "counters are the classic thing a redelivery corrupts");
    }

    [Fact]
    public async Task EscalatingARisk_MovesItOutOfTheOpenCount()
    {
        await using var host = await ReportingHost.StartAsync(bus =>
        {
            bus.AddConsumer<RiskRaisedProjection>();
            bus.AddConsumer<RiskEscalatedProjection>();
        });

        var riskId = Guid.NewGuid();
        await host.PublishAsync(new RiskRaised { RiskId = riskId, ProjectId = ProjectId, Title = "Supplier risk", Score = 25, Tier = "Critical" });
        await host.Harness.Consumed.Any<RiskRaised>();

        await host.PublishAsync(new RiskEscalated { RiskId = riskId, ProjectId = ProjectId, Title = "Supplier risk", Tier = "Critical", Reason = "missed the date" });
        await host.Harness.Consumed.Any<RiskEscalated>();

        var row = await host.ReadAsync(ProjectId);
        row!.OpenRisks.ShouldBe(0, "a materialised risk is an issue now, not an open risk");
        row.CriticalOpenRisks.ShouldBe(0);
        row.EscalatedRisks.ShouldBe(1);
    }

    [Fact]
    public async Task ACriticalIssue_TurnsTheRowRed()
    {
        await using var host = await ReportingHost.StartAsync(bus =>
        {
            bus.AddConsumer<ProjectDraftCreatedProjection>();
            bus.AddConsumer<IssueRaisedProjection>();
        });

        await host.PublishAsync(ADraft(ProjectId));
        await host.Harness.Consumed.Any<ProjectDraftCreated>();

        await host.PublishAsync(new IssueRaised
        {
            IssueId = Guid.NewGuid(), ProjectId = ProjectId, Title = "Supplier missed the date", Severity = "Critical"
        });
        await host.Harness.Consumed.Any<IssueRaised>();

        var row = await host.ReadAsync(ProjectId);
        row!.CriticalOpenIssues.ShouldBe(1);
        row.OverallStatus.ShouldBe("Red", "the verdict is computed from the copies, not stored");
    }

    [Fact]
    public async Task ResolvingTheIssue_TakesTheRowBackToGreen()
    {
        await using var host = await ReportingHost.StartAsync(bus =>
        {
            bus.AddConsumer<ProjectDraftCreatedProjection>();
            bus.AddConsumer<IssueRaisedProjection>();
            bus.AddConsumer<IssueResolvedProjection>();
        });

        await host.PublishAsync(ADraft(ProjectId));
        await host.Harness.Consumed.Any<ProjectDraftCreated>();

        var issueId = Guid.NewGuid();
        await host.PublishAsync(new IssueRaised { IssueId = issueId, ProjectId = ProjectId, Title = "Supplier missed the date", Severity = "Critical" });
        await host.Harness.Consumed.Any<IssueRaised>();

        await host.PublishAsync(new IssueResolved { IssueId = issueId, ProjectId = ProjectId });
        await host.Harness.Consumed.Any<IssueResolved>();

        var row = await host.ReadAsync(ProjectId);
        row!.OpenIssues.ShouldBe(0);
        row.CriticalOpenIssues.ShouldBe(0);
        row.OverallStatus.ShouldBe("Green");
    }

    [Fact]
    public async Task BenefitRealisation_UsesTheRunningTotalSoARedeliveryCannotDoubleCount()
    {
        await using var host = await ReportingHost.StartAsync(bus => bus.AddConsumer<BenefitRealisedProjection>());

        var realised = new BenefitRealised
        {
            ProjectId = ProjectId, ProfileId = Guid.NewGuid(),
            ActualValue = 100_000m, RealisedToDate = 100_000m, RealisationPercent = 28.57m
        };

        await host.PublishAsync(realised, Guid.NewGuid());
        await host.Harness.Consumed.Any<BenefitRealised>();

        // A different message id, so the inbox does not suppress it. The projection is still
        // safe because the event carries the resulting total rather than only the delta.
        await host.PublishAsync(realised, Guid.NewGuid());
        await Task.Delay(200);

        var row = await host.ReadAsync(ProjectId);
        row!.BenefitRealised.ShouldBe(100_000m);
        row.RealisationPercent.ShouldBe(28.57m);
    }

    [Fact]
    public async Task AFailedInitiation_ClearsTheCopiesThatCompensationRemovedUpstream()
    {
        await using var host = await ReportingHost.StartAsync(bus =>
        {
            bus.AddConsumer<KpiScorecardProvisionedProjection>();
            bus.AddConsumer<ProjectInitiationFailedProjection>();
        });

        await host.PublishAsync(new KpiScorecardProvisioned
        {
            ProjectId = ProjectId, ScorecardId = Guid.NewGuid(), ProjectCode = "PRJ-0099", KpiCount = 3
        });
        await host.Harness.Consumed.Any<KpiScorecardProvisioned>();

        await host.PublishAsync(new ProjectInitiationFailed
        {
            ProjectId = ProjectId, Code = "PRJ-0099", Reason = "Forecast exceeds the portfolio ceiling"
        });
        await host.Harness.Consumed.Any<ProjectInitiationFailed>();

        var row = await host.ReadAsync(ProjectId);
        row!.Stage.ShouldBe("InitiationFailed");
        row.KpiNotMeasured.ShouldBe(0, "compensation deleted the real scorecard, so the copy must go too");
        row.BenefitForecast.ShouldBe(0);
        row.OverallStatus.ShouldBe("Failed");
    }

    [Fact]
    public async Task AKpiMeasurement_MovesTheKpiBetweenRagBuckets()
    {
        await using var host = await ReportingHost.StartAsync(bus =>
        {
            bus.AddConsumer<KpiScorecardProvisionedProjection>();
            bus.AddConsumer<KpiMeasurementRecordedProjection>();
        });

        await host.PublishAsync(new KpiScorecardProvisioned
        {
            ProjectId = ProjectId, ScorecardId = Guid.NewGuid(), ProjectCode = "PRJ-0007", KpiCount = 3
        });
        await host.Harness.Consumed.Any<KpiScorecardProvisioned>();

        var kpiId = Guid.NewGuid();
        await host.PublishAsync(new KpiMeasurementRecorded
        {
            KpiId = kpiId, ProjectId = ProjectId, KpiName = "Benefit realisation", Value = 40m, Rag = "Red"
        });
        await host.Harness.Consumed.Any<KpiMeasurementRecorded>();

        var afterFirst = await host.ReadAsync(ProjectId);
        afterFirst!.KpiNotMeasured.ShouldBe(2);
        afterFirst.KpiRed.ShouldBe(1);

        await host.PublishAsync(new KpiMeasurementRecorded
        {
            KpiId = kpiId, ProjectId = ProjectId, KpiName = "Benefit realisation", Value = 100m, Rag = "Green"
        });
        await host.Harness.Consumed.Any<KpiMeasurementRecorded>(x => x.Context.Message.Rag == "Green");

        var afterRecovery = await host.ReadAsync(ProjectId);
        afterRecovery!.KpiRed.ShouldBe(0);
        afterRecovery.KpiGreen.ShouldBe(1);
        afterRecovery.KpiNotMeasured.ShouldBe(2, "the other two KPIs still have no reading");
    }

    [Fact]
    public async Task ClosingAProject_LeavesTheRowVisibleButClosed()
    {
        await using var host = await ReportingHost.StartAsync(bus =>
        {
            bus.AddConsumer<ProjectDraftCreatedProjection>();
            bus.AddConsumer<ProjectClosedProjection>();
        });

        await host.PublishAsync(ADraft(ProjectId));
        await host.Harness.Consumed.Any<ProjectDraftCreated>();

        await host.PublishAsync(new ProjectClosed { ProjectId = ProjectId, Code = "PRJ-0007" });
        await host.Harness.Consumed.Any<ProjectClosed>();

        var row = await host.ReadAsync(ProjectId);
        row!.Stage.ShouldBe("Closed");
        row.OverallStatus.ShouldBe("Closed", "a closed project is not judged Green or Red");
    }
}
