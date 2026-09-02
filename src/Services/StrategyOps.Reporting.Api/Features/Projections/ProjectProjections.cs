using StrategyOps.BuildingBlocks.Inbox;
using StrategyOps.BuildingBlocks.Time;
using StrategyOps.Contracts.V1.Projects;
using StrategyOps.Reporting.Api.Domain;
using StrategyOps.Reporting.Api.Hubs;
using StrategyOps.Reporting.Api.Infrastructure;

namespace StrategyOps.Reporting.Api.Features.Projections;

public sealed class ProjectDraftCreatedProjection(
    ReportingDbContext db, IInboxStore inbox, IPortfolioNotifier notifier, IClock clock, ILogger<ProjectDraftCreatedProjection> logger)
    : PortfolioProjection<ProjectDraftCreated>(db, inbox, notifier, clock, logger)
{
    protected override Guid ProjectIdOf(ProjectDraftCreated message) => message.ProjectId;

    protected override void Apply(PortfolioScorecard scorecard, ProjectDraftCreated message)
    {
        scorecard.ProjectCode = message.Code;
        scorecard.ProjectName = message.Name;
        scorecard.Budget = message.Budget;
        scorecard.Stage = "Draft";
    }
}

public sealed class ProjectActivatedProjection(
    ReportingDbContext db, IInboxStore inbox, IPortfolioNotifier notifier, IClock clock, ILogger<ProjectActivatedProjection> logger)
    : PortfolioProjection<ProjectActivated>(db, inbox, notifier, clock, logger)
{
    protected override Guid ProjectIdOf(ProjectActivated message) => message.ProjectId;

    protected override void Apply(PortfolioScorecard scorecard, ProjectActivated message)
    {
        scorecard.ProjectCode = message.Code;
        scorecard.Stage = "Active";
    }
}

public sealed class ProjectInitiationFailedProjection(
    ReportingDbContext db, IInboxStore inbox, IPortfolioNotifier notifier, IClock clock, ILogger<ProjectInitiationFailedProjection> logger)
    : PortfolioProjection<ProjectInitiationFailed>(db, inbox, notifier, clock, logger)
{
    protected override Guid ProjectIdOf(ProjectInitiationFailed message) => message.ProjectId;

    protected override void Apply(PortfolioScorecard scorecard, ProjectInitiationFailed message)
    {
        scorecard.ProjectCode = message.Code;
        scorecard.Stage = "InitiationFailed";
        scorecard.HealthReason = message.Reason;

        // Compensation removed the scorecard, register and profile in the source services,
        // so the copies here have to go too - otherwise the dashboard shows a failed project
        // still carrying three KPIs and a benefit forecast.
        scorecard.KpiTotal = 0;
        scorecard.KpiGreen = scorecard.KpiAmber = scorecard.KpiRed = scorecard.KpiNotMeasured = 0;
        scorecard.BenefitForecast = scorecard.BenefitRealised = scorecard.RealisationPercent = 0;
        scorecard.BenefitStatus = "None";
    }
}

public sealed class ProjectHealthChangedProjection(
    ReportingDbContext db, IInboxStore inbox, IPortfolioNotifier notifier, IClock clock, ILogger<ProjectHealthChangedProjection> logger)
    : PortfolioProjection<ProjectHealthChanged>(db, inbox, notifier, clock, logger)
{
    protected override Guid ProjectIdOf(ProjectHealthChanged message) => message.ProjectId;

    protected override void Apply(PortfolioScorecard scorecard, ProjectHealthChanged message)
    {
        scorecard.ProjectCode = message.Code;
        scorecard.Health = message.Health;
        scorecard.HealthReason = message.Reason;
    }
}

public sealed class ProjectClosedProjection(
    ReportingDbContext db, IInboxStore inbox, IPortfolioNotifier notifier, IClock clock, ILogger<ProjectClosedProjection> logger)
    : PortfolioProjection<ProjectClosed>(db, inbox, notifier, clock, logger)
{
    protected override Guid ProjectIdOf(ProjectClosed message) => message.ProjectId;

    protected override void Apply(PortfolioScorecard scorecard, ProjectClosed message)
    {
        scorecard.ProjectCode = message.Code;
        scorecard.Stage = "Closed";
    }
}
