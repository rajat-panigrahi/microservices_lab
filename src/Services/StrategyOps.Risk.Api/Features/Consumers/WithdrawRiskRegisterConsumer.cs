using MassTransit;
using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Inbox;
using StrategyOps.BuildingBlocks.Messaging;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.Contracts.V1.Sagas;
using StrategyOps.Contracts.V1.Risks;
using StrategyOps.Risk.Api.Infrastructure;

namespace StrategyOps.Risk.Api.Features.Consumers;

/// <summary>
/// Compensation. Initiation failed somewhere else, so the register this service created has
/// to go away again.
/// </summary>
/// <remarks>
/// This is what "rollback" means once a transaction spans services: not an undo, but a
/// deliberate, business-visible reversal. Note that it is written to be safe when there is
/// nothing to undo - compensation gets redelivered like anything else, and the second run
/// must be a no-op rather than an error.
/// </remarks>
public sealed class WithdrawRiskRegisterConsumer(
    RiskDbContext db,
    IInboxStore inbox,
    IOutboxWriter outbox,
    ILogger<WithdrawRiskRegisterConsumer> logger)
    : IdempotentConsumer<RiskDbContext, WithdrawRiskRegister>(db, inbox, logger)
{
    protected override async Task ConsumeOnceAsync(ConsumeContext<WithdrawRiskRegister> context)
    {
        var message = context.Message;

        var register = await Db.Registers
            .FirstOrDefaultAsync(r => r.ProjectId == message.ProjectId, context.CancellationToken);

        if (register is null)
        {
            // Nothing to undo - but the saga is still waiting to hear that this leg's
            // compensation finished. Answering only when there was work to do would hang the
            // saga in exactly the cases that are hardest to reproduce.
            outbox.Enqueue(new RiskRegisterWithdrawn
            {
                ProjectId = message.ProjectId,
                CorrelationId = message.CorrelationId
            });

            Logger.LogInformation("Nothing to withdraw for project {ProjectId}: no register was created", message.ProjectId);
            return;
        }

        var risks = await Db.Risks
            .Where(r => r.RegisterId == register.Id)
            .ToListAsync(context.CancellationToken);

        Db.Risks.RemoveRange(risks);
        Db.Registers.Remove(register);

        outbox.Enqueue(new RiskRegisterWithdrawn
        {
            ProjectId = message.ProjectId,
            CorrelationId = message.CorrelationId
        });

        Logger.LogInformation("Withdrew the risk register for {ProjectCode} after failed initiation", register.ProjectCode);
    }
}
