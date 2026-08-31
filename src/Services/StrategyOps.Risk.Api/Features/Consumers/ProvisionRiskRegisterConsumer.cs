using MassTransit;
using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Inbox;
using StrategyOps.BuildingBlocks.Messaging;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.BuildingBlocks.Time;
using StrategyOps.Contracts.V1.Sagas;
using StrategyOps.Contracts.V1.Risks;
using StrategyOps.Risk.Api.Domain;
using StrategyOps.Risk.Api.Infrastructure;

namespace StrategyOps.Risk.Api.Features.Consumers;

/// <summary>
/// The Risk service's leg of project initiation: provision the register, then report back.
/// </summary>
/// <remarks>
/// <para>
/// This consumes a <b>command</b> from the saga, not an event. The difference is not
/// cosmetic: the saga is waiting for this specific service to answer, and it will compensate
/// the other two legs if the answer is a failure or never arrives.
/// </para>
/// <para>
/// Reporting back is therefore mandatory. A consumer that quietly did the work and published
/// nothing would leave the saga waiting until its timeout, and the project would fail
/// initiation despite everything having succeeded. Every saga participant owes the
/// orchestrator an answer - success or failure, but always an answer.
/// </para>
/// </remarks>
public sealed class ProvisionRiskRegisterConsumer(
    RiskDbContext db,
    IInboxStore inbox,
    IOutboxWriter outbox,
    IClock clock,
    ILogger<ProvisionRiskRegisterConsumer> logger)
    : IdempotentConsumer<RiskDbContext, ProvisionRiskRegister>(db, inbox, logger)
{
    protected override async Task ConsumeOnceAsync(ConsumeContext<ProvisionRiskRegister> context)
    {
        var message = context.Message;

        // Belt and braces alongside the inbox: the unique index on ProjectId means even a
        // message that somehow slipped past deduplication cannot create a second register.
        var existing = await Db.Registers
            .FirstOrDefaultAsync(r => r.ProjectId == message.ProjectId, context.CancellationToken);

        if (existing is not null)
        {
            Logger.LogInformation(
                "Project {ProjectCode} already has a risk register; re-confirming to the saga",
                message.ProjectCode);

            Outbox(new RiskRegisterProvisioned
            {
                ProjectId = message.ProjectId,
                RegisterId = existing.Id,
                ProjectCode = message.ProjectCode,
                CorrelationId = message.CorrelationId
            });

            return;
        }

        try
        {
            var register = RiskRegister.Provision(message.ProjectId, message.ProjectCode, clock.UtcNow);
            Db.Registers.Add(register);

            Outbox(new RiskRegisterProvisioned
            {
                ProjectId = message.ProjectId,
                RegisterId = register.Id,
                ProjectCode = message.ProjectCode,
                CorrelationId = message.CorrelationId
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failure here is a business outcome the saga has to hear about, not an
            // exception to retry forever. Report it and let the saga compensate the legs
            // that did succeed.
            Logger.LogError(ex, "Could not provision a risk register for {ProjectCode}", message.ProjectCode);

            Outbox(new RiskRegisterProvisionFailed
            {
                ProjectId = message.ProjectId,
                Reason = ex.Message,
                CorrelationId = message.CorrelationId
            });
        }

        void Outbox(StrategyOps.Contracts.V1.IntegrationEvent @event) => outbox.Enqueue(@event);
    }
}
