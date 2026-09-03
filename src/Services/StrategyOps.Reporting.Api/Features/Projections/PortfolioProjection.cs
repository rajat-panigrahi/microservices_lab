using MassTransit;
using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Inbox;
using StrategyOps.BuildingBlocks.Messaging;
using StrategyOps.BuildingBlocks.Time;
using StrategyOps.Reporting.Api.Domain;
using StrategyOps.Reporting.Api.Hubs;
using StrategyOps.Reporting.Api.Infrastructure;

namespace StrategyOps.Reporting.Api.Features.Projections;

/// <summary>
/// Base for every projection: find or create the row for a project, let the subclass change
/// it, then stamp it and push it to the dashboard.
/// </summary>
/// <remarks>
/// <para>
/// Projections are <b>upserts</b>, never inserts. Events arrive out of order across five
/// independent services - a KPI confirmation can easily land before the ProjectDraftCreated
/// that "created" the project - so a projection that assumed the row already existed would
/// drop data whenever the network reordered anything. Creating the row on demand makes
/// ordering irrelevant for everything except fields that genuinely overwrite each other.
/// </para>
/// <para>
/// Idempotency comes from the inbox in the base class, so a redelivered event cannot
/// double-count a risk or an issue.
/// </para>
/// </remarks>
public abstract class PortfolioProjection<TMessage>(
    ReportingDbContext db,
    IInboxStore inbox,
    IPortfolioNotifier notifier,
    IClock clock,
    ILogger logger)
    : IdempotentConsumer<ReportingDbContext, TMessage>(db, inbox, logger)
    where TMessage : class
{
    protected abstract Guid ProjectIdOf(TMessage message);

    protected abstract void Apply(PortfolioScorecard scorecard, TMessage message);

    /// <summary>
    /// Override when a projection needs to touch more than the scorecard row - the KPI
    /// projections keep per-KPI status alongside it. Defaults to the synchronous
    /// <see cref="Apply"/>.
    /// </summary>
    protected virtual Task ApplyAsync(PortfolioScorecard scorecard, TMessage message, CancellationToken cancellationToken)
    {
        Apply(scorecard, message);
        return Task.CompletedTask;
    }

    protected override async Task ConsumeOnceAsync(ConsumeContext<TMessage> context)
    {
        var projectId = ProjectIdOf(context.Message);

        var scorecard = await Db.Scorecards
            .FirstOrDefaultAsync(s => s.ProjectId == projectId, context.CancellationToken);

        if (scorecard is null)
        {
            scorecard = new PortfolioScorecard { ProjectId = projectId };
            Db.Scorecards.Add(scorecard);
        }

        await ApplyAsync(scorecard, context.Message, context.CancellationToken);
        scorecard.LastUpdatedUtc = clock.UtcNow;

        // The push happens after the base class commits, via the notifier being called here
        // and SignalR delivering asynchronously. A viewer briefly seeing a value that is
        // rolled back is acceptable for a dashboard; blocking the commit on a websocket
        // fan-out would not be.
        await notifier.ScorecardChangedAsync(scorecard, context.CancellationToken);
    }
}
