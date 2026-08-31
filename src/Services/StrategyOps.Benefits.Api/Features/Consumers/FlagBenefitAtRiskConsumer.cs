using MassTransit;
using Microsoft.EntityFrameworkCore;
using StrategyOps.Benefits.Api.Infrastructure;
using StrategyOps.BuildingBlocks.Inbox;
using StrategyOps.BuildingBlocks.Messaging;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.Contracts.V1.Benefits;
using StrategyOps.Contracts.V1.Issues;
using StrategyOps.Contracts.V1.Kpis;
using StrategyOps.Contracts.V1.Projects;

namespace StrategyOps.Benefits.Api.Features.Consumers;

/// <summary>
/// The fourth reaction in the choreographed chain: a critical issue means the forecast value
/// is in doubt.
/// </summary>
/// <remarks>
/// Neither the Risk service nor the Issues service knows this consumer exists. That is the
/// point of choreography - and also its cost, since "what happens when a risk escalates?" now
/// has an answer spread across four services.
/// </remarks>
public sealed class FlagBenefitAtRiskOnIssueRaisedConsumer(
    BenefitsDbContext db,
    IInboxStore inbox,
    IOutboxWriter outbox,
    ILogger<FlagBenefitAtRiskOnIssueRaisedConsumer> logger)
    : IdempotentConsumer<BenefitsDbContext, IssueRaised>(db, inbox, logger)
{
    protected override async Task ConsumeOnceAsync(ConsumeContext<IssueRaised> context)
    {
        var message = context.Message;

        // Only a critical issue puts the whole forecast in doubt. A service that reacted to
        // every issue would flag every benefit permanently, and the flag would stop meaning
        // anything.
        if (message.Severity != "Critical")
        {
            return;
        }

        var profile = await Db.Profiles
            .FirstOrDefaultAsync(p => p.ProjectId == message.ProjectId, context.CancellationToken);

        if (profile is null || !profile.FlagAtRisk($"Critical issue raised: {message.Title}"))
        {
            return;
        }

        outbox.Enqueue(new BenefitAtRisk
        {
            ProjectId = profile.ProjectId,
            ProfileId = profile.Id,
            Reason = profile.AtRiskReason!,
            CorrelationId = message.CorrelationId
        });

        Logger.LogWarning("Benefit forecast for {ProjectCode} flagged at risk", profile.ProjectCode);
    }
}

/// <summary>A KPI going off Green is the other signal that forecast value is in doubt.</summary>
public sealed class FlagBenefitAtRiskOnKpiBreachedConsumer(
    BenefitsDbContext db,
    IInboxStore inbox,
    IOutboxWriter outbox,
    ILogger<FlagBenefitAtRiskOnKpiBreachedConsumer> logger)
    : IdempotentConsumer<BenefitsDbContext, KpiBreached>(db, inbox, logger)
{
    protected override async Task ConsumeOnceAsync(ConsumeContext<KpiBreached> context)
    {
        var message = context.Message;

        if (message.Rag != "Red")
        {
            return;
        }

        var profile = await Db.Profiles
            .FirstOrDefaultAsync(p => p.ProjectId == message.ProjectId, context.CancellationToken);

        if (profile is null || !profile.FlagAtRisk($"KPI '{message.KpiName}' is Red at {message.Value} against a target of {message.Target}"))
        {
            return;
        }

        outbox.Enqueue(new BenefitAtRisk
        {
            ProjectId = profile.ProjectId,
            ProfileId = profile.Id,
            Reason = profile.AtRiskReason!,
            CorrelationId = message.CorrelationId
        });
    }
}

/// <summary>The project finished; the benefit profile stops accepting new realisation.</summary>
public sealed class CloseBenefitProfileOnProjectClosedConsumer(
    BenefitsDbContext db,
    IInboxStore inbox,
    ILogger<CloseBenefitProfileOnProjectClosedConsumer> logger)
    : IdempotentConsumer<BenefitsDbContext, ProjectClosed>(db, inbox, logger)
{
    protected override async Task ConsumeOnceAsync(ConsumeContext<ProjectClosed> context)
    {
        var profile = await Db.Profiles
            .FirstOrDefaultAsync(p => p.ProjectId == context.Message.ProjectId, context.CancellationToken);

        if (profile is null)
        {
            return;
        }

        profile.Close();

        Logger.LogInformation(
            "Closed the benefit profile for {ProjectCode} at {Percent}% realisation",
            profile.ProjectCode,
            profile.RealisationPercent);
    }
}
