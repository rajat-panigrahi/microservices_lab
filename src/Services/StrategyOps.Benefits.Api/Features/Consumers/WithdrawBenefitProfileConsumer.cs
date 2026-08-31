using MassTransit;
using Microsoft.EntityFrameworkCore;
using StrategyOps.Benefits.Api.Infrastructure;
using StrategyOps.BuildingBlocks.Inbox;
using StrategyOps.BuildingBlocks.Messaging;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.Contracts.V1.Benefits;
using StrategyOps.Contracts.V1.Sagas;

namespace StrategyOps.Benefits.Api.Features.Consumers;

public sealed class WithdrawBenefitProfileConsumer(
    BenefitsDbContext db,
    IInboxStore inbox,
    IOutboxWriter outbox,
    ILogger<WithdrawBenefitProfileConsumer> logger)
    : IdempotentConsumer<BenefitsDbContext, WithdrawBenefitProfile>(db, inbox, logger)
{
    protected override async Task ConsumeOnceAsync(ConsumeContext<WithdrawBenefitProfile> context)
    {
        var profile = await Db.Profiles
            .FirstOrDefaultAsync(p => p.ProjectId == context.Message.ProjectId, context.CancellationToken);

        if (profile is not null)
        {
            var realisations = await Db.Realisations
                .Where(r => r.ProfileId == profile.Id)
                .ToListAsync(context.CancellationToken);

            Db.Realisations.RemoveRange(realisations);
            Db.Profiles.Remove(profile);

            Logger.LogInformation("Withdrew the benefit profile for {ProjectCode}", profile.ProjectCode);
        }

        // Confirm either way - the saga is waiting on this leg regardless of whether there
        // was anything to undo.
        outbox.Enqueue(new BenefitProfileWithdrawn
        {
            ProjectId = context.Message.ProjectId,
            CorrelationId = context.Message.CorrelationId
        });
    }
}
