using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Api;
using StrategyOps.BuildingBlocks.Results;
using StrategyOps.Kpi.Api.Domain;
using StrategyOps.Kpi.Api.Infrastructure;

namespace StrategyOps.Kpi.Api.Features.GetScorecard;

public sealed record KpiView(
    Guid Id,
    string Name,
    string Unit,
    string Direction,
    decimal Target,
    decimal AmberThreshold,
    decimal? LatestValue,
    string Rag);

public sealed record ScorecardView(
    Guid ScorecardId,
    Guid ProjectId,
    string ProjectCode,
    string Status,
    int GreenCount,
    int AmberCount,
    int RedCount,
    int NotMeasuredCount,
    IReadOnlyList<KpiView> Kpis);

public sealed class GetScorecardHandler(KpiDbContext db)
{
    public async Task<Result<ScorecardView>> HandleAsync(Guid projectId, CancellationToken ct)
    {
        var scorecard = await db.Scorecards.AsNoTracking().FirstOrDefaultAsync(s => s.ProjectId == projectId, ct);

        if (scorecard is null)
        {
            return Result<ScorecardView>.NotFound("kpi.scorecard_not_found", $"Project '{projectId}' has no scorecard.");
        }

        var kpis = await db.Kpis
            .AsNoTracking()
            .Where(k => k.ScorecardId == scorecard.Id)
            .OrderBy(k => k.Name)
            .Select(k => new KpiView(
                k.Id,
                k.Name,
                k.Unit,
                k.Direction.ToString(),
                k.Target,
                k.AmberThreshold,
                k.LatestValue,
                k.Rag.ToString()))
            .ToListAsync(ct);

        return Result<ScorecardView>.Ok(new ScorecardView(
            scorecard.Id,
            scorecard.ProjectId,
            scorecard.ProjectCode,
            scorecard.Status.ToString(),
            kpis.Count(k => k.Rag == nameof(KpiRag.Green)),
            kpis.Count(k => k.Rag == nameof(KpiRag.Amber)),
            kpis.Count(k => k.Rag == nameof(KpiRag.Red)),
            kpis.Count(k => k.Rag == nameof(KpiRag.NotMeasured)),
            kpis));
    }
}

public sealed class GetScorecardEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/projects/{projectId:guid}/scorecard", async (
                Guid projectId,
                GetScorecardHandler handler,
                CancellationToken ct) => (await handler.HandleAsync(projectId, ct)).ToHttpResult())
            .WithName("GetScorecard")
            .WithSummary("A project's KPI scorecard with RAG counts")
            .WithTags("KPIs")
            .Produces<ScorecardView>()
            .ProducesProblem(StatusCodes.Status404NotFound);
}
