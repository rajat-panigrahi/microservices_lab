using MassTransit;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using StrategyOps.Contracts.V1.Issues;
using StrategyOps.Contracts.V1.Projects;
using StrategyOps.Contracts.V1.Risks;
using StrategyOps.Issues.Api.Domain;
using StrategyOps.Issues.Api.Features.Consumers;
using StrategyOps.Issues.Api.Infrastructure;
using StrategyOps.Projects.Api.Domain;
using StrategyOps.Projects.Api.Features.Consumers;
using StrategyOps.Projects.Api.Infrastructure;

namespace StrategyOps.Messaging.Tests.Choreography;

/// <summary>
/// The choreographed chain, tested one link at a time:
/// risk escalates -&gt; Issues raises an issue -&gt; Projects drops RAG status.
/// Each link is a separate service reacting on its own, so each is tested on its own.
/// </summary>
public class RiskEscalationChainTests
{
    private static readonly Guid ProjectId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);

    private static RiskEscalated AnEscalation(Guid riskId, string tier = "Critical") => new()
    {
        RiskId = riskId,
        ProjectId = ProjectId,
        Title = "Supplier cannot meet the integration deadline",
        Tier = tier,
        Reason = "Supplier confirmed they will miss the date",
        CorrelationId = "corr-123"
    };

    [Fact]
    public async Task EscalatingARisk_RaisesAnIssueInTheIssuesService()
    {
        await using var host = await ConsumerHost<IssuesDbContext>.StartAsync(
            bus => bus.AddConsumer<RaiseIssueOnRiskEscalatedConsumer>(), Now);

        var riskId = Guid.NewGuid();
        await host.PublishAsync(AnEscalation(riskId), Guid.NewGuid());

        (await host.Harness.Consumed.Any<RiskEscalated>()).ShouldBeTrue();

        var issue = await host.QueryAsync(db => db.Issues.SingleOrDefaultAsync(i => i.OriginRiskId == riskId));

        issue.ShouldNotBeNull();
        issue.Severity.ShouldBe(IssueSeverity.Critical, "a Critical risk becomes a Critical issue");
        issue.Title.ShouldStartWith("[Escalated]");
        issue.TargetResolutionUtc.ShouldBe(Now.AddDays(2), "Critical issues carry a 2-day SLA");
    }

    [Fact]
    public async Task EscalatingARisk_PublishesIssueRaisedForTheNextLinkInTheChain()
    {
        await using var host = await ConsumerHost<IssuesDbContext>.StartAsync(
            bus => bus.AddConsumer<RaiseIssueOnRiskEscalatedConsumer>(), Now);

        var riskId = Guid.NewGuid();
        await host.PublishAsync(AnEscalation(riskId), Guid.NewGuid());
        await host.Harness.Consumed.Any<RiskEscalated>();

        // The consumer stages the event in its own outbox rather than publishing inline -
        // so the assertion is on the outbox, which is where the event genuinely is at the
        // moment the transaction commits.
        var queued = await host.QueryAsync(db => db.OutboxMessages
            .Where(m => m.Type == typeof(IssueRaised).FullName)
            .ToListAsync());

        queued.Count.ShouldBe(1);
        queued[0].CorrelationId.ShouldBe("corr-123", "the correlation id survives the hop");
    }

    [Fact]
    public async Task TheSameEscalationDeliveredTwice_RaisesOnlyOneIssue()
    {
        await using var host = await ConsumerHost<IssuesDbContext>.StartAsync(
            bus => bus.AddConsumer<RaiseIssueOnRiskEscalatedConsumer>(), Now);

        var riskId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var escalation = AnEscalation(riskId);

        // At-least-once delivery means this genuinely happens: the outbox publisher can
        // crash after the broker accepted the message but before the row was marked sent.
        await host.PublishAsync(escalation, messageId);
        await host.Harness.Consumed.Any<RiskEscalated>();
        await host.PublishAsync(escalation, messageId);

        await Task.Delay(200);

        var issues = await host.QueryAsync(db => db.Issues.CountAsync(i => i.OriginRiskId == riskId));
        var raisedEvents = await host.QueryAsync(db => db.OutboxMessages.CountAsync(m => m.Type == typeof(IssueRaised).FullName));

        issues.ShouldBe(1, "the inbox suppressed the redelivery");
        raisedEvents.ShouldBe(1, "and no second event fanned out");
    }

    [Fact]
    public async Task TwoDifferentEscalationsOfTheSameRisk_StillRaiseOnlyOneIssue()
    {
        await using var host = await ConsumerHost<IssuesDbContext>.StartAsync(
            bus => bus.AddConsumer<RaiseIssueOnRiskEscalatedConsumer>(), Now);

        var riskId = Guid.NewGuid();

        // Distinct message ids, so the inbox does not catch this one - the guard is the
        // business rule ("one issue per risk") plus the unique index behind it. Idempotency
        // needs both: infrastructure dedup for redeliveries, and a domain rule for genuine
        // duplicates that happen to carry different ids.
        await host.PublishAsync(AnEscalation(riskId), Guid.NewGuid());
        await host.Harness.Consumed.Any<RiskEscalated>();
        await host.PublishAsync(AnEscalation(riskId), Guid.NewGuid());

        await Task.Delay(200);

        var issues = await host.QueryAsync(db => db.Issues.CountAsync(i => i.OriginRiskId == riskId));
        issues.ShouldBe(1);
    }

    [Theory]
    [InlineData("Critical", IssueSeverity.Critical)]
    [InlineData("High", IssueSeverity.High)]
    [InlineData("Medium", IssueSeverity.Medium)]
    [InlineData("a-tier-this-version-does-not-know", IssueSeverity.Medium)]
    public async Task RiskTierMapsOntoIssueSeverity_IncludingATierThisServiceDoesNotRecognise(
        string tier,
        IssueSeverity expected)
    {
        await using var host = await ConsumerHost<IssuesDbContext>.StartAsync(
            bus => bus.AddConsumer<RaiseIssueOnRiskEscalatedConsumer>(), Now);

        var riskId = Guid.NewGuid();
        await host.PublishAsync(AnEscalation(riskId, tier), Guid.NewGuid());
        await host.Harness.Consumed.Any<RiskEscalated>();

        var issue = await host.QueryAsync(db => db.Issues.SingleAsync(i => i.OriginRiskId == riskId));
        issue.Severity.ShouldBe(expected);
    }

    [Fact]
    public async Task RaisingAnIssue_DropsTheProjectToAmberOrRed()
    {
        await using var host = await ConsumerHost<ProjectsDbContext>.StartAsync(
            bus => bus.AddConsumer<DropHealthOnIssueRaisedConsumer>(), Now);

        var projectId = await SeedActiveProjectAsync(host);

        await host.PublishAsync(
            new IssueRaised
            {
                IssueId = Guid.NewGuid(),
                ProjectId = projectId,
                Title = "[Escalated] Supplier cannot meet the integration deadline",
                Severity = "Critical",
                CorrelationId = "corr-123"
            },
            Guid.NewGuid());

        (await host.Harness.Consumed.Any<IssueRaised>()).ShouldBeTrue();

        var project = await host.QueryAsync(db => db.Projects.SingleAsync(p => p.Id == projectId));
        project.Health.ShouldBe(ProjectHealth.Red, "a Critical issue takes the project red");

        var published = await host.QueryAsync(db => db.OutboxMessages
            .CountAsync(m => m.Type == typeof(ProjectHealthChanged).FullName));
        published.ShouldBe(1);
    }

    [Fact]
    public async Task ANonCriticalIssue_TakesTheProjectAmberRatherThanRed()
    {
        await using var host = await ConsumerHost<ProjectsDbContext>.StartAsync(
            bus => bus.AddConsumer<DropHealthOnIssueRaisedConsumer>(), Now);

        var projectId = await SeedActiveProjectAsync(host);

        await host.PublishAsync(
            new IssueRaised
            {
                IssueId = Guid.NewGuid(),
                ProjectId = projectId,
                Title = "Minor data quality problem",
                Severity = "Medium"
            },
            Guid.NewGuid());

        await host.Harness.Consumed.Any<IssueRaised>();

        var project = await host.QueryAsync(db => db.Projects.SingleAsync(p => p.Id == projectId));
        project.Health.ShouldBe(ProjectHealth.Amber);
    }

    [Fact]
    public async Task AMediumIssueAfterACriticalOne_DoesNotImproveTheProjectHealth()
    {
        await using var host = await ConsumerHost<ProjectsDbContext>.StartAsync(
            bus => bus.AddConsumer<DropHealthOnIssueRaisedConsumer>(), Now);

        var projectId = await SeedActiveProjectAsync(host);

        await host.PublishAsync(
            new IssueRaised { IssueId = Guid.NewGuid(), ProjectId = projectId, Title = "Critical", Severity = "Critical" },
            Guid.NewGuid());
        await host.Harness.Consumed.Any<IssueRaised>();

        await host.PublishAsync(
            new IssueRaised { IssueId = Guid.NewGuid(), ProjectId = projectId, Title = "Medium", Severity = "Medium" },
            Guid.NewGuid());

        await Task.Delay(200);

        var project = await host.QueryAsync(db => db.Projects.SingleAsync(p => p.Id == projectId));
        project.Health.ShouldBe(ProjectHealth.Red, "health does not recover just because a lesser issue arrived");
    }

    [Fact]
    public async Task TheSameIssueRaisedTwice_PublishesOnlyOneHealthChange()
    {
        await using var host = await ConsumerHost<ProjectsDbContext>.StartAsync(
            bus => bus.AddConsumer<DropHealthOnIssueRaisedConsumer>(), Now);

        var projectId = await SeedActiveProjectAsync(host);
        var messageId = Guid.NewGuid();
        var raised = new IssueRaised
        {
            IssueId = Guid.NewGuid(),
            ProjectId = projectId,
            Title = "[Escalated] Supplier cannot meet the integration deadline",
            Severity = "Critical"
        };

        await host.PublishAsync(raised, messageId);
        await host.Harness.Consumed.Any<IssueRaised>();
        await host.PublishAsync(raised, messageId);

        await Task.Delay(200);

        var published = await host.QueryAsync(db => db.OutboxMessages
            .CountAsync(m => m.Type == typeof(ProjectHealthChanged).FullName));

        published.ShouldBe(1);
    }

    [Fact]
    public async Task AnIssueForAnUnknownProject_IsIgnoredRatherThanCrashingTheConsumer()
    {
        await using var host = await ConsumerHost<ProjectsDbContext>.StartAsync(
            bus => bus.AddConsumer<DropHealthOnIssueRaisedConsumer>(), Now);

        await host.PublishAsync(
            new IssueRaised
            {
                IssueId = Guid.NewGuid(),
                ProjectId = Guid.NewGuid(),
                Title = "Issue against a project this service has never heard of",
                Severity = "High"
            },
            Guid.NewGuid());

        (await host.Harness.Consumed.Any<IssueRaised>()).ShouldBeTrue();

        // Nothing was published, and nothing threw - a message that cannot be acted on must
        // not become a poison message that blocks the queue behind it.
        var published = await host.QueryAsync(db => db.OutboxMessages.CountAsync());
        published.ShouldBe(0);
    }

    private static async Task<Guid> SeedActiveProjectAsync(ConsumerHost<ProjectsDbContext> host)
    {
        var objective = StrategicObjective.Create("SO-01", "Reduce operating cost by 15%", "FY27", "COO");
        var project = Project.CreateDraft("PRJ-0007", "Warehouse automation", objective.Id, "A. Sponsor", 250_000m, Now);
        project.SubmitForInitiation(Now);
        project.CompleteInitiation(Now);

        await host.SeedAsync(async db =>
        {
            db.Objectives.Add(objective);
            db.Projects.Add(project);
            await Task.CompletedTask;
        });

        return project.Id;
    }
}
