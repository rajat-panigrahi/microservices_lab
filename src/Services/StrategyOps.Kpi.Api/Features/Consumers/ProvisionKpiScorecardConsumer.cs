using MassTransit;
using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Inbox;
using StrategyOps.BuildingBlocks.Messaging;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.BuildingBlocks.Time;
using StrategyOps.Contracts.V1;
using StrategyOps.Contracts.V1.Kpis;
using StrategyOps.Contracts.V1.Sagas;
using StrategyOps.Kpi.Api.Domain;
using StrategyOps.Kpi.Api.Infrastructure;

namespace StrategyOps.Kpi.Api.Features.Consumers;

/// <summary>
/// The KPI service's leg of project initiation: create the scorecard and seed the baseline
/// measures, then answer the saga.
/// </summary>
public sealed class ProvisionKpiScorecardConsumer(
    KpiDbContext db,
    IInboxStore inbox,
    IOutboxWriter outbox,
    IClock clock,
    ILogger<ProvisionKpiScorecardConsumer> logger)
    : IdempotentConsumer<KpiDbContext, ProvisionKpiScorecard>(db, inbox, logger)
{
    protected override async Task ConsumeOnceAsync(ConsumeContext<ProvisionKpiScorecard> context)
    {
        var message = context.Message;

        var existing = await Db.Scorecards
            .FirstOrDefaultAsync(s => s.ProjectId == message.ProjectId, context.CancellationToken);

        if (existing is not null)
        {
            var kpiCount = await Db.Kpis.CountAsync(k => k.ScorecardId == existing.Id, context.CancellationToken);

            // Re-confirm rather than staying silent: a saga that missed the first
            // confirmation would otherwise wait for its timeout and compensate a project
            // that is in fact perfectly set up.
            Enqueue(new KpiScorecardProvisioned
            {
                ProjectId = message.ProjectId,
                ScorecardId = existing.Id,
                ProjectCode = message.ProjectCode,
                KpiCount = kpiCount,
                CorrelationId = message.CorrelationId
            });

            return;
        }

        try
        {
            var scorecard = KpiScorecard.Provision(message.ProjectId, message.ProjectCode, message.ObjectiveId, clock.UtcNow);
            Db.Scorecards.Add(scorecard);

            var baseline = KpiScorecard.BaselineKpisFor(scorecard.Id).ToList();
            Db.Kpis.AddRange(baseline);

            Enqueue(new KpiScorecardProvisioned
            {
                ProjectId = message.ProjectId,
                ScorecardId = scorecard.Id,
                ProjectCode = message.ProjectCode,
                KpiCount = baseline.Count,
                CorrelationId = message.CorrelationId
            });

            Logger.LogInformation(
                "Provisioned a scorecard with {KpiCount} baseline KPIs for {ProjectCode}",
                baseline.Count,
                message.ProjectCode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogError(ex, "Could not provision a scorecard for {ProjectCode}", message.ProjectCode);

            Enqueue(new KpiScorecardProvisionFailed
            {
                ProjectId = message.ProjectId,
                Reason = ex.Message,
                CorrelationId = message.CorrelationId
            });
        }

        void Enqueue(IntegrationEvent @event) => outbox.Enqueue(@event);
    }
}
