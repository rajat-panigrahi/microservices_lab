using MassTransit;
using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Inbox;
using StrategyOps.BuildingBlocks.Messaging;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.BuildingBlocks.Time;
using StrategyOps.Contracts.V1.Issues;
using StrategyOps.Contracts.V1.Risks;
using StrategyOps.Issues.Api.Domain;
using StrategyOps.Issues.Api.Infrastructure;

namespace StrategyOps.Issues.Api.Features.Consumers;

/// <summary>
/// A risk materialised, so an issue exists now. This is the second link in the choreography.
/// </summary>
/// <remarks>
/// <para>
/// Nobody told this service to do this. The Risk service published a fact; this service
/// decided, on its own, that the fact means an issue. Add a fifth service tomorrow that also
/// cares about escalations and neither Risk nor Issues changes at all - that is the appeal
/// of choreography.
/// </para>
/// <para>
/// The cost shows up the first time someone asks "what happens when a risk escalates?".
/// The answer is not in any one file; you have to know to grep for consumers of
/// <see cref="RiskEscalated"/> across four services. That is the trade-off the orchestrated
/// saga in Projects makes differently.
/// </para>
/// </remarks>
public sealed class RaiseIssueOnRiskEscalatedConsumer(
    IssuesDbContext db,
    IInboxStore inbox,
    IOutboxWriter outbox,
    IClock clock,
    ILogger<RaiseIssueOnRiskEscalatedConsumer> logger)
    : IdempotentConsumer<IssuesDbContext, RiskEscalated>(db, inbox, logger)
{
    protected override async Task ConsumeOnceAsync(ConsumeContext<RiskEscalated> context)
    {
        var message = context.Message;

        var alreadyRaised = await Db.Issues
            .AnyAsync(i => i.OriginRiskId == message.RiskId, context.CancellationToken);

        if (alreadyRaised)
        {
            Logger.LogInformation("Risk {RiskId} already has an issue; ignoring", message.RiskId);
            return;
        }

        var issue = Issue.RaiseFromRisk(
            message.ProjectId,
            message.RiskId,
            $"[Escalated] {message.Title}",
            message.Tier,
            clock.UtcNow);

        Db.Issues.Add(issue);

        outbox.Enqueue(new IssueRaised
        {
            IssueId = issue.Id,
            ProjectId = issue.ProjectId,
            OriginRiskId = issue.OriginRiskId,
            Title = issue.Title,
            Severity = issue.Severity.ToString(),
            CorrelationId = message.CorrelationId
        });

        Logger.LogInformation(
            "Raised {Severity} issue {IssueId} from escalated risk {RiskId}",
            issue.Severity,
            issue.Id,
            message.RiskId);
    }
}
