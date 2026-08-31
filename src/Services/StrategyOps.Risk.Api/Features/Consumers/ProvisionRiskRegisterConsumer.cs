using MassTransit;
using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Inbox;
using StrategyOps.BuildingBlocks.Messaging;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.BuildingBlocks.Time;
using StrategyOps.Contracts.V1.Projects;
using StrategyOps.Contracts.V1.Risks;
using StrategyOps.Risk.Api.Domain;
using StrategyOps.Risk.Api.Infrastructure;

namespace StrategyOps.Risk.Api.Features.Consumers;

/// <summary>
/// The Risk service's leg of project initiation: provision the register, then report back.
/// </summary>
/// <remarks>
/// Reporting back matters. In phase 3 the saga in the Projects service waits for
/// <see cref="RiskRegisterProvisioned"/> from here, a scorecard confirmation from KPI and a
/// benefit profile confirmation from Benefits before the project becomes Active - and it
/// compensates all three if any of them reports failure instead. A consumer that just did
/// the work silently would leave the saga waiting forever.
/// </remarks>
public sealed class ProvisionRiskRegisterConsumer(
    RiskDbContext db,
    IInboxStore inbox,
    IOutboxWriter outbox,
    IClock clock,
    ILogger<ProvisionRiskRegisterConsumer> logger)
    : IdempotentConsumer<RiskDbContext, ProjectInitiationRequested>(db, inbox, logger)
{
    protected override async Task ConsumeOnceAsync(ConsumeContext<ProjectInitiationRequested> context)
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
                message.Code);

            Outbox(new RiskRegisterProvisioned
            {
                ProjectId = message.ProjectId,
                RegisterId = existing.Id,
                ProjectCode = message.Code,
                CorrelationId = message.CorrelationId
            });

            return;
        }

        try
        {
            var register = RiskRegister.Provision(message.ProjectId, message.Code, clock.UtcNow);
            Db.Registers.Add(register);

            Outbox(new RiskRegisterProvisioned
            {
                ProjectId = message.ProjectId,
                RegisterId = register.Id,
                ProjectCode = message.Code,
                CorrelationId = message.CorrelationId
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failure here is a business outcome the saga has to hear about, not an
            // exception to retry forever. Report it and let the saga compensate the legs
            // that did succeed.
            Logger.LogError(ex, "Could not provision a risk register for {ProjectCode}", message.Code);

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
