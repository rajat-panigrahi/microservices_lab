using System.Net;
using System.Net.Http.Json;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using StrategyOps.BuildingBlocks.Api;
using StrategyOps.BuildingBlocks.Auth;

namespace StrategyOps.Gateway.Features.PortfolioOverview;

public sealed record SectionStatus<T>(T? Data, bool Available, string? Error)
{
    public static SectionStatus<T> Ok(T? data) => new(data, true, null);

    public static SectionStatus<T> Unavailable(string error) => new(default, false, error);
}

public sealed record ProjectOverview(
    object? Project,
    SectionStatus<object> Scorecard,
    SectionStatus<object> RiskRegister,
    SectionStatus<object> Issues,
    SectionStatus<object> Benefits,
    int ElapsedMs);

/// <summary>
/// One call that fans out to four services in parallel and returns whatever came back.
/// </summary>
/// <remarks>
/// <para>
/// This is the aggregation an API gateway exists for: without it a mobile client makes five
/// round trips over a slow network to render one screen. With it, one round trip to the edge
/// and four fast ones inside the datacentre.
/// </para>
/// <para>
/// Two things make it safe to use, and both are the interesting part:
/// </para>
/// <list type="number">
///   <item><b>The calls run in parallel.</b> Sequentially the response time is the sum of
///   four services; in parallel it is the slowest one.</item>
///   <item><b>A failing section degrades, it does not fail the request.</b> If Benefits is
///   down the caller still gets the project, its KPIs, its risks and its issues, with
///   <c>benefits.available = false</c>. An aggregation endpoint that returns 500 because one
///   of four dependencies is unhealthy has multiplied the platform's failure rate by four
///   rather than hiding it.</item>
/// </list>
/// <para>
/// Behind each call sits the Polly pipeline: retry with jitter, a circuit breaker, and
/// timeouts. Once the breaker for a service opens, that section fails <em>instantly</em>
/// rather than after a timeout - which is what keeps this endpoint fast while a dependency
/// is unhealthy, instead of merely correct.
/// </para>
/// </remarks>
public sealed class PortfolioOverviewHandler(IHttpClientFactory httpClientFactory, ILogger<PortfolioOverviewHandler> logger)
{
    public async Task<IResult> HandleAsync(Guid projectId, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        var projectTask = FetchAsync("projects", $"http://projects-api/projects/{projectId}", ct);
        var scorecardTask = FetchAsync("kpi", $"http://kpi-api/projects/{projectId}/scorecard", ct);
        var riskTask = FetchAsync("risk", $"http://risk-api/projects/{projectId}/risk-register", ct);
        var issuesTask = FetchAsync("issues", $"http://issues-api/issues?projectId={projectId}", ct);
        var benefitsTask = FetchAsync("benefits", $"http://benefits-api/projects/{projectId}/benefits", ct);

        await Task.WhenAll(projectTask, scorecardTask, riskTask, issuesTask, benefitsTask);

        var project = await projectTask;

        // The project itself is the one section that cannot degrade: without it there is
        // nothing to show, and a 404 here means a 404 for the whole request.
        if (!project.Available || project.Data is null)
        {
            return Results.Problem(
                title: "Resource was not found",
                detail: $"Project '{projectId}' could not be loaded: {project.Error ?? "not found"}",
                statusCode: StatusCodes.Status404NotFound);
        }

        return Results.Ok(new ProjectOverview(
            project.Data,
            await scorecardTask,
            await riskTask,
            await issuesTask,
            await benefitsTask,
            (int)stopwatch.ElapsedMilliseconds));
    }

    private async Task<SectionStatus<object>> FetchAsync(string clientName, string url, CancellationToken ct)
    {
        try
        {
            using var client = httpClientFactory.CreateClient(clientName);
            var response = await client.GetAsync(url, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // Legitimately absent - a draft project has no scorecard yet. Available, just empty.
                return SectionStatus<object>.Ok(null);
            }

            if (!response.IsSuccessStatusCode)
            {
                return SectionStatus<object>.Unavailable($"{(int)response.StatusCode} {response.ReasonPhrase}");
            }

            return SectionStatus<object>.Ok(await response.Content.ReadFromJsonAsync<object>(ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Includes BrokenCircuitException once the breaker has opened, which is why this
            // path is fast rather than slow while a dependency is unhealthy.
            logger.LogWarning("Overview section {Client} unavailable: {Message}", clientName, ex.Message);
            return SectionStatus<object>.Unavailable(ex.GetType().Name);
        }
    }
}

public sealed class PortfolioOverviewEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/api/portfolio/{projectId:guid}/overview", async (
                Guid projectId,
                PortfolioOverviewHandler handler,
                CancellationToken ct) => await handler.HandleAsync(projectId, ct))
            .WithName("GetPortfolioOverview")
            .WithSummary("One project across all five services, fanned out in parallel, degrading section by section")
            .WithTags("Gateway")
            .RequireAuthorization(Policies.Read)
            .Produces<ProjectOverview>();
}
