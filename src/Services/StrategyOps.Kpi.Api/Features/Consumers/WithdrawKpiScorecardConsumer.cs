using MassTransit;
using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Inbox;
using StrategyOps.BuildingBlocks.Messaging;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.Contracts.V1.Kpis;
using StrategyOps.Contracts.V1.Sagas;
using StrategyOps.Kpi.Api.Infrastructure;

namespace StrategyOps.Kpi.Api.Features.Consumers;

/// <summary>
/// Compensation: initiation failed elsewhere, so the scorecard this service created is
/// removed and the saga is told so.
/// </summary>
public sealed class WithdrawKpiScorecardConsumer(
    KpiDbContext db,
    IInboxStore inbox,
    IOutboxWriter outbox,
    ILogger<WithdrawKpiScorecardConsumer> logger)
    : IdempotentConsumer<KpiDbContext, WithdrawKpiScorecard>(db, inbox, logger)
{
    protected override async Task ConsumeOnceAsync(ConsumeContext<WithdrawKpiScorecard> context)
    {
        var scorecard = await Db.Scorecards
            .FirstOrDefaultAsync(s => s.ProjectId == context.Message.ProjectId, context.CancellationToken);

        if (scorecard is null)
        {
            // Nothing to undo, but the saga is still waiting to hear that compensation
            // finished - so confirm anyway, or it hangs until its timeout.
            outbox.Enqueue(new KpiScorecardWithdrawn
            {
                ProjectId = context.Message.ProjectId,
                CorrelationId = context.Message.CorrelationId
            });
            return;
        }

        var kpis = await Db.Kpis.Where(k => k.ScorecardId == scorecard.Id).ToListAsync(context.CancellationToken);
        var kpiIds = kpis.Select(k => k.Id).ToList();
        var measurements = await Db.Measurements.Where(m => kpiIds.Contains(m.KpiId)).ToListAsync(context.CancellationToken);

        Db.Measurements.RemoveRange(measurements);
        Db.Kpis.RemoveRange(kpis);
        Db.Scorecards.Remove(scorecard);

        outbox.Enqueue(new KpiScorecardWithdrawn
        {
            ProjectId = context.Message.ProjectId,
            CorrelationId = context.Message.CorrelationId
        });

        Logger.LogInformation("Withdrew the scorecard for {ProjectCode} after failed initiation", scorecard.ProjectCode);
    }
}
