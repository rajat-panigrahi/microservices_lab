using MassTransit;
using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Inbox;
using StrategyOps.BuildingBlocks.Messaging;
using StrategyOps.Contracts.V1.Projects;
using StrategyOps.Kpi.Api.Infrastructure;

namespace StrategyOps.Kpi.Api.Features.Consumers;

public sealed class CloseScorecardOnProjectClosedConsumer(
    KpiDbContext db,
    IInboxStore inbox,
    ILogger<CloseScorecardOnProjectClosedConsumer> logger)
    : IdempotentConsumer<KpiDbContext, ProjectClosed>(db, inbox, logger)
{
    protected override async Task ConsumeOnceAsync(ConsumeContext<ProjectClosed> context)
    {
        var scorecard = await Db.Scorecards
            .FirstOrDefaultAsync(s => s.ProjectId == context.Message.ProjectId, context.CancellationToken);

        scorecard?.Close();

        if (scorecard is not null)
        {
            Logger.LogInformation("Closed the scorecard for {ProjectCode}", context.Message.Code);
        }
    }
}
