using MassTransit;
using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Inbox;
using StrategyOps.BuildingBlocks.Messaging;
using StrategyOps.Contracts.V1.Projects;
using StrategyOps.Risk.Api.Infrastructure;

namespace StrategyOps.Risk.Api.Features.Consumers;

/// <summary>
/// The project finished, so its register stops accepting new risks. The Projects service
/// does not reach into this database to do it - it announces, and this service decides what
/// closing means on its side.
/// </summary>
public sealed class CloseRiskRegisterConsumer(
    RiskDbContext db,
    IInboxStore inbox,
    ILogger<CloseRiskRegisterConsumer> logger)
    : IdempotentConsumer<RiskDbContext, ProjectClosed>(db, inbox, logger)
{
    protected override async Task ConsumeOnceAsync(ConsumeContext<ProjectClosed> context)
    {
        var register = await Db.Registers
            .FirstOrDefaultAsync(r => r.ProjectId == context.Message.ProjectId, context.CancellationToken);

        if (register is null)
        {
            return;
        }

        register.Close();
        Logger.LogInformation("Closed the risk register for {ProjectCode}", context.Message.Code);
    }
}
