using MassTransit;
using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Inbox;
using StrategyOps.BuildingBlocks.Messaging;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.Contracts.V1.Issues;
using StrategyOps.Contracts.V1.Projects;
using StrategyOps.Projects.Api.Domain;
using StrategyOps.Projects.Api.Infrastructure;

namespace StrategyOps.Projects.Api.Features.Consumers;

/// <summary>
/// The third link in the choreography: an issue was raised, so the project's RAG status
/// reflects it.
/// </summary>
/// <remarks>
/// Worth noticing what this consumer does NOT do. It does not ask the Issues service how
/// many open issues the project has, and it does not recompute health from scratch - both
/// would make the Projects service depend on Issues being up in order to process a message.
/// It reacts to the fact in front of it and nothing else.
///
/// The aggregate's SetHealth returns whether anything changed, so a redelivered IssueRaised
/// cannot emit a second ProjectHealthChanged - idempotency enforced by the domain rather
/// than only by the inbox.
/// </remarks>
public sealed class DropHealthOnIssueRaisedConsumer(
    ProjectsDbContext db,
    IInboxStore inbox,
    IOutboxWriter outbox,
    ILogger<DropHealthOnIssueRaisedConsumer> logger)
    : IdempotentConsumer<ProjectsDbContext, IssueRaised>(db, inbox, logger)
{
    protected override async Task ConsumeOnceAsync(ConsumeContext<IssueRaised> context)
    {
        var message = context.Message;

        var project = await Db.Projects
            .FirstOrDefaultAsync(p => p.Id == message.ProjectId, context.CancellationToken);

        if (project is null)
        {
            Logger.LogWarning("Issue {IssueId} references unknown project {ProjectId}", message.IssueId, message.ProjectId);
            return;
        }

        if (project.Stage == ProjectStage.Closed)
        {
            return;
        }

        // A critical issue takes the project red; anything else takes it amber, but never
        // back up - health only recovers when the underlying issue is resolved.
        var target = message.Severity == "Critical" ? ProjectHealth.Red : ProjectHealth.Amber;

        if (target <= project.Health)
        {
            return;
        }

        if (!project.SetHealth(target, $"Issue raised: {message.Title}"))
        {
            return;
        }

        outbox.Enqueue(new ProjectHealthChanged
        {
            ProjectId = project.Id,
            Code = project.Code,
            Health = project.Health.ToString(),
            Reason = $"Issue raised: {message.Title}",
            CorrelationId = message.CorrelationId
        });

        Logger.LogInformation(
            "Project {ProjectCode} moved to {Health} because a {Severity} issue was raised",
            project.Code,
            project.Health,
            message.Severity);
    }
}
