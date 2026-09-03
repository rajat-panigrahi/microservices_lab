using MassTransit;
using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Inbox;
using StrategyOps.BuildingBlocks.Messaging;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.Contracts.V1.Issues;
using StrategyOps.Contracts.V1.Risks;
using StrategyOps.Risk.Api.Domain;
using StrategyOps.Risk.Api.Infrastructure;

namespace StrategyOps.Risk.Api.Features.Consumers;

/// <summary>
/// Closes the loop: when the issue a risk turned into is resolved, the risk is retired too.
/// </summary>
/// <remarks>
/// This is the return leg of the choreography, and it is why the Issues service carries
/// OriginRiskId on its events - without it, Issues would have to call back into Risk to ask
/// "which risk did this come from?", turning an event notification into a request/response
/// dependency in the wrong direction.
/// </remarks>
public sealed class CloseRiskOnIssueResolvedConsumer(
    RiskDbContext db,
    IInboxStore inbox,
    IOutboxWriter outbox,
    ILogger<CloseRiskOnIssueResolvedConsumer> logger)
    : IdempotentConsumer<RiskDbContext, IssueResolved>(db, inbox, logger)
{
    protected override async Task ConsumeOnceAsync(ConsumeContext<IssueResolved> context)
    {
        var message = context.Message;

        if (message.OriginRiskId is null)
        {
            // Raised directly rather than escalated from a risk; nothing to close here.
            return;
        }

        var risk = await Db.Risks
            .FirstOrDefaultAsync(r => r.Id == message.OriginRiskId.Value, context.CancellationToken);

        if (risk is null || risk.Status == RiskStatus.Closed)
        {
            return;
        }

        risk.Close($"Resolved via issue {message.IssueId}.");

        outbox.Enqueue(new RiskClosed
        {
            RiskId = risk.Id,
            ProjectId = message.ProjectId,
            CorrelationId = message.CorrelationId
        });

        Logger.LogInformation("Closed risk {RiskId} because issue {IssueId} was resolved", risk.Id, message.IssueId);
    }
}
