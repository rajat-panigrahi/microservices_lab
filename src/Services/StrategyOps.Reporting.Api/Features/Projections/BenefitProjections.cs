using StrategyOps.BuildingBlocks.Inbox;
using StrategyOps.BuildingBlocks.Time;
using StrategyOps.Contracts.V1.Benefits;
using StrategyOps.Reporting.Api.Domain;
using StrategyOps.Reporting.Api.Hubs;
using StrategyOps.Reporting.Api.Infrastructure;

namespace StrategyOps.Reporting.Api.Features.Projections;

public sealed class BenefitProfileRegisteredProjection(
    ReportingDbContext db, IInboxStore inbox, IPortfolioNotifier notifier, IClock clock, ILogger<BenefitProfileRegisteredProjection> logger)
    : PortfolioProjection<BenefitProfileRegistered>(db, inbox, notifier, clock, logger)
{
    protected override Guid ProjectIdOf(BenefitProfileRegistered message) => message.ProjectId;

    protected override void Apply(PortfolioScorecard scorecard, BenefitProfileRegistered message)
    {
        scorecard.ProjectCode = message.ProjectCode;
        scorecard.BenefitForecast = message.ForecastValue;
        scorecard.BenefitStatus = "Registered";
    }
}

public sealed class BenefitProfileWithdrawnProjection(
    ReportingDbContext db, IInboxStore inbox, IPortfolioNotifier notifier, IClock clock, ILogger<BenefitProfileWithdrawnProjection> logger)
    : PortfolioProjection<BenefitProfileWithdrawn>(db, inbox, notifier, clock, logger)
{
    protected override Guid ProjectIdOf(BenefitProfileWithdrawn message) => message.ProjectId;

    protected override void Apply(PortfolioScorecard scorecard, BenefitProfileWithdrawn message)
    {
        scorecard.BenefitForecast = 0;
        scorecard.BenefitRealised = 0;
        scorecard.RealisationPercent = 0;
        scorecard.BenefitStatus = "None";
    }
}

public sealed class BenefitRealisedProjection(
    ReportingDbContext db, IInboxStore inbox, IPortfolioNotifier notifier, IClock clock, ILogger<BenefitRealisedProjection> logger)
    : PortfolioProjection<BenefitRealised>(db, inbox, notifier, clock, logger)
{
    protected override Guid ProjectIdOf(BenefitRealised message) => message.ProjectId;

    protected override void Apply(PortfolioScorecard scorecard, BenefitRealised message)
    {
        // The event carries the running total, not just the increment, so this projection
        // is naturally idempotent on value: a redelivery sets the same number rather than
        // adding twice. Carrying the resulting state as well as the delta is a small
        // contract decision that removes a whole class of double-counting bug.
        scorecard.BenefitRealised = message.RealisedToDate;
        scorecard.RealisationPercent = message.RealisationPercent;
        scorecard.BenefitStatus = "Realising";
    }
}

public sealed class BenefitAtRiskProjection(
    ReportingDbContext db, IInboxStore inbox, IPortfolioNotifier notifier, IClock clock, ILogger<BenefitAtRiskProjection> logger)
    : PortfolioProjection<BenefitAtRisk>(db, inbox, notifier, clock, logger)
{
    protected override Guid ProjectIdOf(BenefitAtRisk message) => message.ProjectId;

    protected override void Apply(PortfolioScorecard scorecard, BenefitAtRisk message) =>
        scorecard.BenefitStatus = "AtRisk";
}
