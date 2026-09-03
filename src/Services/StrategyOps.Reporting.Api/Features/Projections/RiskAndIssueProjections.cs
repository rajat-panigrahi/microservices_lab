using StrategyOps.BuildingBlocks.Inbox;
using StrategyOps.BuildingBlocks.Time;
using StrategyOps.Contracts.V1.Issues;
using StrategyOps.Contracts.V1.Risks;
using StrategyOps.Reporting.Api.Domain;
using StrategyOps.Reporting.Api.Hubs;
using StrategyOps.Reporting.Api.Infrastructure;

namespace StrategyOps.Reporting.Api.Features.Projections;

public sealed class RiskRaisedProjection(
    ReportingDbContext db, IInboxStore inbox, IPortfolioNotifier notifier, IClock clock, ILogger<RiskRaisedProjection> logger)
    : PortfolioProjection<RiskRaised>(db, inbox, notifier, clock, logger)
{
    protected override Guid ProjectIdOf(RiskRaised message) => message.ProjectId;

    protected override void Apply(PortfolioScorecard scorecard, RiskRaised message)
    {
        scorecard.OpenRisks++;

        if (message.Tier == "Critical")
        {
            scorecard.CriticalOpenRisks++;
        }
    }
}

public sealed class RiskEscalatedProjection(
    ReportingDbContext db, IInboxStore inbox, IPortfolioNotifier notifier, IClock clock, ILogger<RiskEscalatedProjection> logger)
    : PortfolioProjection<RiskEscalated>(db, inbox, notifier, clock, logger)
{
    protected override Guid ProjectIdOf(RiskEscalated message) => message.ProjectId;

    protected override void Apply(PortfolioScorecard scorecard, RiskEscalated message)
    {
        scorecard.EscalatedRisks++;

        // An escalated risk has materialised - it is an issue now, not an open risk.
        if (scorecard.OpenRisks > 0)
        {
            scorecard.OpenRisks--;
        }

        if (message.Tier == "Critical" && scorecard.CriticalOpenRisks > 0)
        {
            scorecard.CriticalOpenRisks--;
        }
    }
}

public sealed class RiskClosedProjection(
    ReportingDbContext db, IInboxStore inbox, IPortfolioNotifier notifier, IClock clock, ILogger<RiskClosedProjection> logger)
    : PortfolioProjection<RiskClosed>(db, inbox, notifier, clock, logger)
{
    protected override Guid ProjectIdOf(RiskClosed message) => message.ProjectId;

    protected override void Apply(PortfolioScorecard scorecard, RiskClosed message)
    {
        // A risk can be closed either straight from open, or after it escalated and its
        // issue was resolved. Only the first case reduces the open count, and the counters
        // are floored because a projection must never go negative on a redelivery it did
        // not expect.
        if (scorecard.EscalatedRisks > 0)
        {
            scorecard.EscalatedRisks--;
        }
        else if (scorecard.OpenRisks > 0)
        {
            scorecard.OpenRisks--;
        }
    }
}

public sealed class IssueRaisedProjection(
    ReportingDbContext db, IInboxStore inbox, IPortfolioNotifier notifier, IClock clock, ILogger<IssueRaisedProjection> logger)
    : PortfolioProjection<IssueRaised>(db, inbox, notifier, clock, logger)
{
    protected override Guid ProjectIdOf(IssueRaised message) => message.ProjectId;

    protected override void Apply(PortfolioScorecard scorecard, IssueRaised message)
    {
        scorecard.OpenIssues++;

        if (message.Severity == "Critical")
        {
            scorecard.CriticalOpenIssues++;
        }
    }
}

public sealed class IssueResolvedProjection(
    ReportingDbContext db, IInboxStore inbox, IPortfolioNotifier notifier, IClock clock, ILogger<IssueResolvedProjection> logger)
    : PortfolioProjection<IssueResolved>(db, inbox, notifier, clock, logger)
{
    protected override Guid ProjectIdOf(IssueResolved message) => message.ProjectId;

    protected override void Apply(PortfolioScorecard scorecard, IssueResolved message)
    {
        if (scorecard.OpenIssues > 0)
        {
            scorecard.OpenIssues--;
        }

        // IssueResolved does not carry severity, so the critical counter is decremented
        // optimistically. That is a real limitation of a thin event, and the honest fix is
        // either to widen the contract or to rebuild - not to call back into Issues, which
        // would turn a notification into a synchronous dependency.
        if (scorecard.CriticalOpenIssues > 0 && scorecard.CriticalOpenIssues > scorecard.OpenIssues)
        {
            scorecard.CriticalOpenIssues--;
        }
    }
}
