using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Api;
using StrategyOps.BuildingBlocks.Auth;
using StrategyOps.BuildingBlocks.Results;
using StrategyOps.Reporting.Api.Infrastructure;

namespace StrategyOps.Reporting.Api.Features.GetPortfolio;

public sealed record ScorecardRow(
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    string Stage,
    string Health,
    string? HealthReason,
    decimal Budget,
    int KpiGreen,
    int KpiAmber,
    int KpiRed,
    int KpiNotMeasured,
    int OpenRisks,
    int CriticalOpenRisks,
    int OpenIssues,
    int CriticalOpenIssues,
    decimal BenefitForecast,
    decimal BenefitRealised,
    decimal RealisationPercent,
    string BenefitStatus,
    string OverallStatus,
    DateTimeOffset LastUpdatedUtc);

public sealed record PortfolioSummary(
    int TotalProjects,
    int Green,
    int Amber,
    int Red,
    decimal TotalForecast,
    decimal TotalRealised,
    IReadOnlyList<ScorecardRow> Projects);

/// <summary>
/// The whole point of the read side: one query, one table, no fan-out.
/// </summary>
/// <remarks>
/// Compare what this would cost without the read model - a call to Projects, then per project
/// a call to KPI, Risk, Issues and Benefits. Twenty projects would be eighty-one HTTP calls,
/// and the page would fail whenever any one of four services was down. Here it is a single
/// indexed SELECT that keeps working even if every other service is offline, at the cost of
/// showing data that may be a second or two stale.
/// </remarks>
public sealed class GetPortfolioHandler(ReportingDbContext db)
{
    public async Task<Result<PortfolioSummary>> HandleAsync(string? stage, CancellationToken ct)
    {
        var query = db.Scorecards.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(stage))
        {
            query = query.Where(s => s.Stage == stage);
        }

        var scorecards = await query.OrderBy(s => s.ProjectCode).ToListAsync(ct);

        var rows = scorecards
            .Select(s => new ScorecardRow(
                s.ProjectId, s.ProjectCode, s.ProjectName, s.Stage, s.Health, s.HealthReason, s.Budget,
                s.KpiGreen, s.KpiAmber, s.KpiRed, s.KpiNotMeasured,
                s.OpenRisks, s.CriticalOpenRisks, s.OpenIssues, s.CriticalOpenIssues,
                s.BenefitForecast, s.BenefitRealised, s.RealisationPercent, s.BenefitStatus,
                s.OverallStatus, s.LastUpdatedUtc))
            .ToList();

        return Result<PortfolioSummary>.Ok(new PortfolioSummary(
            rows.Count,
            rows.Count(r => r.OverallStatus == "Green"),
            rows.Count(r => r.OverallStatus == "Amber"),
            rows.Count(r => r.OverallStatus == "Red"),
            rows.Sum(r => r.BenefitForecast),
            rows.Sum(r => r.BenefitRealised),
            rows));
    }
}

public sealed class GetPortfolioEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/reporting/portfolio", async (
                string? stage,
                GetPortfolioHandler handler,
                CancellationToken ct) => (await handler.HandleAsync(stage, ct)).ToHttpResult())
            .WithName("GetPortfolio")
            .WithSummary("Every project with its KPIs, risks, issues and benefits, from one table")
            .WithTags("Reporting")
            .RequireAuthorization(Policies.Read)
            .Produces<PortfolioSummary>();

        app.MapGet("/reporting/portfolio/{projectId:guid}", async (
                Guid projectId,
                ReportingDbContext db,
                CancellationToken ct) =>
            {
                var scorecard = await db.Scorecards.AsNoTracking().FirstOrDefaultAsync(s => s.ProjectId == projectId, ct);

                return scorecard is null
                    ? Results.Problem(title: "Resource was not found", statusCode: StatusCodes.Status404NotFound)
                    : Results.Ok(scorecard);
            })
            .WithName("GetPortfolioProject")
            .WithSummary("One project's denormalised row")
            .WithTags("Reporting")
            .RequireAuthorization(Policies.Read);
    }
}
