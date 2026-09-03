using MassTransit;
using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Inbox;
using StrategyOps.BuildingBlocks.Messaging;
using StrategyOps.Contracts.V1.Projects;
using StrategyOps.Issues.Api.Domain;
using StrategyOps.Issues.Api.Infrastructure;

namespace StrategyOps.Issues.Api.Features.Consumers;

/// <summary>
/// A closed project cannot have issues worked against it, so any that are already resolved
/// get closed off. Unresolved ones are deliberately left alone - closing a project does not
/// make its open problems disappear, and silently discarding them would hide exactly the
/// information a post-implementation review needs.
/// </summary>
public sealed class CloseIssuesOnProjectClosedConsumer(
    IssuesDbContext db,
    IInboxStore inbox,
    ILogger<CloseIssuesOnProjectClosedConsumer> logger)
    : IdempotentConsumer<IssuesDbContext, ProjectClosed>(db, inbox, logger)
{
    protected override async Task ConsumeOnceAsync(ConsumeContext<ProjectClosed> context)
    {
        var resolved = await Db.Issues
            .Where(i => i.ProjectId == context.Message.ProjectId && i.Status == IssueStatus.Resolved)
            .ToListAsync(context.CancellationToken);

        foreach (var issue in resolved)
        {
            issue.Close();
        }

        var stillOpen = await Db.Issues
            .CountAsync(
                i => i.ProjectId == context.Message.ProjectId
                     && i.Status != IssueStatus.Resolved
                     && i.Status != IssueStatus.Closed,
                context.CancellationToken);

        if (stillOpen > 0)
        {
            Logger.LogWarning(
                "Project {ProjectCode} closed with {OpenIssueCount} unresolved issues; leaving them open",
                context.Message.Code,
                stillOpen);
        }
    }
}
