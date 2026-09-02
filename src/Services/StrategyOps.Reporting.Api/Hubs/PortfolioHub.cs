using Microsoft.AspNetCore.SignalR;
using StrategyOps.Reporting.Api.Domain;

namespace StrategyOps.Reporting.Api.Hubs;

/// <summary>
/// Pushes read-model changes to any browser watching the dashboard.
/// </summary>
/// <remarks>
/// This is what makes eventual consistency visible rather than theoretical: escalate a risk
/// in one terminal and watch the row go red a second or two later, without refreshing. The
/// delay you can see is the outbox poll plus the broker hop.
/// </remarks>
public sealed class PortfolioHub : Hub
{
    public const string Path = "/hubs/portfolio";

    public const string ScorecardUpdated = "scorecardUpdated";
}

/// <summary>
/// Wraps the hub so projections depend on an interface rather than on SignalR itself -
/// which also means a projection can be tested without a hub context.
/// </summary>
public interface IPortfolioNotifier
{
    Task ScorecardChangedAsync(PortfolioScorecard scorecard, CancellationToken cancellationToken);
}

public sealed class SignalRPortfolioNotifier(IHubContext<PortfolioHub> hub) : IPortfolioNotifier
{
    public Task ScorecardChangedAsync(PortfolioScorecard scorecard, CancellationToken cancellationToken) =>
        hub.Clients.All.SendAsync(
            PortfolioHub.ScorecardUpdated,
            new
            {
                scorecard.ProjectId,
                scorecard.ProjectCode,
                scorecard.ProjectName,
                scorecard.Stage,
                scorecard.Health,
                scorecard.KpiGreen,
                scorecard.KpiAmber,
                scorecard.KpiRed,
                scorecard.OpenRisks,
                scorecard.CriticalOpenRisks,
                scorecard.OpenIssues,
                scorecard.CriticalOpenIssues,
                scorecard.BenefitForecast,
                scorecard.BenefitRealised,
                scorecard.RealisationPercent,
                scorecard.BenefitStatus,
                scorecard.OverallStatus,
                scorecard.LastUpdatedUtc
            },
            cancellationToken);
}
