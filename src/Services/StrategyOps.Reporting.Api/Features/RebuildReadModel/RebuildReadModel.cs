using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StrategyOps.BuildingBlocks.Api;
using StrategyOps.BuildingBlocks.Auth;
using StrategyOps.BuildingBlocks.Results;
using StrategyOps.BuildingBlocks.Time;
using StrategyOps.Reporting.Api.Domain;
using StrategyOps.Reporting.Api.Infrastructure;

namespace StrategyOps.Reporting.Api.Features.RebuildReadModel;

public sealed record RebuildResult(int ProjectsRebuilt, int Failures, IReadOnlyList<string> Errors, TimeSpan Duration);

/// <summary>
/// Throws the read model away and rebuilds it from the source services.
/// </summary>
/// <remarks>
/// <para>
/// This is the answer to the question CQRS always attracts: "what happens when your
/// projection is wrong?" A read model is a <b>cache with a schema</b>. It holds no truth of
/// its own, so when a projection has a bug, or a consumer was down long enough to lose
/// messages past the broker's retention, or the shape of the row needs to change, the fix is
/// to discard it and build it again.
/// </para>
/// <para>
/// Being able to say that in an interview - and to point at the endpoint - is worth far more
/// than describing CQRS in the abstract. The follow-up is usually "how do you rebuild?", and
/// there are two honest answers: replay from an event store, if you kept every event; or
/// re-read current state from the owning services, which is what this does. This system has
/// an outbox, not an event store, so it is the second.
/// </para>
/// <para>
/// The failure handling matters too: one unreachable service degrades one section of one row
/// rather than failing the whole rebuild, and the response reports exactly what could not be
/// refreshed.
/// </para>
/// </remarks>
public sealed class RebuildReadModelHandler(
    ReportingDbContext db,
    IHttpClientFactory httpClientFactory,
    IOptions<UpstreamServices> upstream,
    IClock clock,
    ILogger<RebuildReadModelHandler> logger)
{
    public async Task<Result<RebuildResult>> HandleAsync(CancellationToken ct)
    {
        var startedAt = clock.UtcNow;
        var errors = new List<string>();
        var services = upstream.Value;
        using var http = httpClientFactory.CreateClient("upstream");

        var page = await GetAsync<UpstreamProjectPage>(http, $"{services.Projects}/projects?pageSize=100", errors, ct);

        if (page is null)
        {
            return Result<RebuildResult>.Unavailable(
                "reporting.projects_unavailable",
                "The Projects service is unreachable, so there is nothing to rebuild from.");
        }

        var existing = await db.Scorecards.ToDictionaryAsync(s => s.ProjectId, ct);
        var rebuilt = 0;

        foreach (var project in page.Items)
        {
            if (!existing.TryGetValue(project.Id, out var scorecard))
            {
                scorecard = new PortfolioScorecard { ProjectId = project.Id };
                db.Scorecards.Add(scorecard);
            }

            scorecard.ProjectCode = project.Code;
            scorecard.ProjectName = project.Name;
            scorecard.Stage = project.Stage;
            scorecard.Health = project.Health;
            scorecard.Budget = project.Budget;
            scorecard.LastUpdatedUtc = clock.UtcNow;

            await RefreshKpisAsync(http, services, scorecard, errors, ct);
            await RefreshRisksAsync(http, services, scorecard, errors, ct);
            await RefreshIssuesAsync(http, services, scorecard, errors, ct);
            await RefreshBenefitsAsync(http, services, scorecard, errors, ct);

            rebuilt++;
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation("Rebuilt {Count} portfolio rows with {Failures} partial failures", rebuilt, errors.Count);

        return Result<RebuildResult>.Ok(new RebuildResult(rebuilt, errors.Count, errors, clock.UtcNow - startedAt));
    }

    private async Task RefreshKpisAsync(HttpClient http, UpstreamServices services, PortfolioScorecard scorecard, List<string> errors, CancellationToken ct)
    {
        var scorecardView = await GetAsync<UpstreamScorecard>(http, $"{services.Kpi}/projects/{scorecard.ProjectId}/scorecard", errors, ct, allowNotFound: true);

        scorecard.KpiTotal = scorecardView?.Kpis.Count ?? 0;
        scorecard.KpiGreen = scorecardView?.GreenCount ?? 0;
        scorecard.KpiAmber = scorecardView?.AmberCount ?? 0;
        scorecard.KpiRed = scorecardView?.RedCount ?? 0;
        scorecard.KpiNotMeasured = scorecardView?.NotMeasuredCount ?? 0;
    }

    private async Task RefreshRisksAsync(HttpClient http, UpstreamServices services, PortfolioScorecard scorecard, List<string> errors, CancellationToken ct)
    {
        var register = await GetAsync<UpstreamRiskRegister>(http, $"{services.Risk}/projects/{scorecard.ProjectId}/risk-register", errors, ct, allowNotFound: true);

        scorecard.OpenRisks = register?.OpenCount ?? 0;
        scorecard.CriticalOpenRisks = register?.CriticalOpenCount ?? 0;
        scorecard.EscalatedRisks = register?.Risks.Count(r => r.Status == "Materialised") ?? 0;
    }

    private async Task RefreshIssuesAsync(HttpClient http, UpstreamServices services, PortfolioScorecard scorecard, List<string> errors, CancellationToken ct)
    {
        var issues = await GetAsync<List<UpstreamIssue>>(http, $"{services.Issues}/issues?projectId={scorecard.ProjectId}", errors, ct, allowNotFound: true);

        var open = issues?.Where(i => i.Status is not ("Resolved" or "Closed")).ToList() ?? [];
        scorecard.OpenIssues = open.Count;
        scorecard.CriticalOpenIssues = open.Count(i => i.Severity == "Critical");
    }

    private async Task RefreshBenefitsAsync(HttpClient http, UpstreamServices services, PortfolioScorecard scorecard, List<string> errors, CancellationToken ct)
    {
        var benefit = await GetAsync<UpstreamBenefit>(http, $"{services.Benefits}/projects/{scorecard.ProjectId}/benefits", errors, ct, allowNotFound: true);

        scorecard.BenefitForecast = benefit?.ForecastValue ?? 0;
        scorecard.BenefitRealised = benefit?.RealisedToDate ?? 0;
        scorecard.RealisationPercent = benefit?.RealisationPercent ?? 0;
        scorecard.BenefitStatus = benefit?.Status ?? "None";
    }

    private async Task<T?> GetAsync<T>(HttpClient http, string url, List<string> errors, CancellationToken ct, bool allowNotFound = false)
        where T : class
    {
        try
        {
            var response = await http.GetAsync(url, ct);

            // A 404 is a legitimate answer here: a draft project has no scorecard yet.
            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<T>(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // One unreachable service degrades one section of one row. Failing the whole
            // rebuild because Benefits is restarting would make this endpoint useless in
            // exactly the situation it is needed.
            errors.Add($"{url}: {ex.Message}");
            logger.LogWarning("Rebuild could not reach {Url}: {Message}", url, ex.Message);
            return null;
        }
    }
}

public sealed class RebuildReadModelEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/reporting/rebuild", async (
                RebuildReadModelHandler handler,
                CancellationToken ct) => (await handler.HandleAsync(ct)).ToHttpResult())
            .WithName("RebuildReadModel")
            .WithSummary("Rebuild the whole read model from the source services")
            .WithTags("Reporting")
            .RequireAuthorization(Policies.ManagePortfolio)
            .Produces<RebuildResult>();
}
