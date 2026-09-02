using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Shouldly;
using StrategyOps.BuildingBlocks.Auth;

namespace StrategyOps.Slice.Tests.Security;

/// <summary>
/// Security asserted as behaviour, not as configuration.
/// </summary>
/// <remarks>
/// These run against the real authentication pipeline with genuinely signed tokens, which is
/// the only way they can catch what actually goes wrong: a route that forgot its policy, a
/// role name typo, a fallback policy that was never applied, or lifetime validation quietly
/// switched off.
/// </remarks>
[Collection(nameof(ProjectsApiCollection))]
public class AuthorizationTests(ProjectsApiFactory factory)
{
    private static object AValidProject(Guid objectiveId) => new
    {
        Code = $"PRJ-{Guid.NewGuid().ToString()[..6]}",
        Name = "Warehouse automation",
        ObjectiveId = objectiveId,
        Sponsor = "A. Sponsor",
        Budget = 250_000m
    };

    private async Task<Guid> AnObjectiveAsync()
    {
        var client = factory.CreateClient().WithRole(Roles.PortfolioDirector);

        var response = await client.PostAsJsonAsync("/objectives", new
        {
            Code = $"SO-{Guid.NewGuid().ToString()[..6]}",
            Title = "Reduce operating cost by 15%",
            Horizon = "FY27",
            Owner = "COO"
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ObjectiveBody>())!.Id;
    }

    [Fact]
    public async Task NoToken_Is401_EvenOnARouteThatNeverMentionedAuthorization()
    {
        var response = await factory.CreateClient().GetAsync("/projects");

        // This is the fallback policy earning its place: the endpoint says nothing about
        // authorization, and it is closed anyway. Forgetting to protect a route is far more
        // common than forgetting to open one, and only one of those is a breach.
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnExpiredToken_Is401()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Expired());

        var response = await client.GetAsync("/projects");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ATokenSignedWithTheWrongKey_Is401()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.WrongKey());

        var response = await client.GetAsync("/projects");

        // The forgery case: a well-formed token claiming PortfolioDirector, signed by
        // someone who does not have the key.
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AGarbageToken_Is401RatherThanA500()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-even-a-jwt");

        var response = await client.GetAsync("/projects");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AViewer_CanRead()
    {
        var client = factory.CreateClient().WithRole(Roles.Viewer);

        var response = await client.GetAsync("/projects");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AViewer_CannotCreateAProject()
    {
        var objectiveId = await AnObjectiveAsync();
        var client = factory.CreateClient().WithRole(Roles.Viewer);

        var response = await client.PostAsJsonAsync("/projects", AValidProject(objectiveId));

        // 403, not 401: we know who they are, they are simply not allowed. Returning 401
        // here would tell an authenticated user to go and log in again, which is confusing
        // and, in a browser, an infinite loop.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ARiskOwner_CannotCreateAProject()
    {
        var objectiveId = await AnObjectiveAsync();
        var client = factory.CreateClient().WithRole(Roles.RiskOwner);

        var response = await client.PostAsJsonAsync("/projects", AValidProject(objectiveId));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AProjectManager_CanCreateAProject_ButCannotInitiateIt()
    {
        var objectiveId = await AnObjectiveAsync();
        var client = factory.CreateClient().WithRole(Roles.ProjectManager);

        var created = await client.PostAsJsonAsync("/projects", AValidProject(objectiveId));
        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        var projectId = (await created.Content.ReadFromJsonAsync<ProjectBody>())!.Id;

        // Initiation commits budget across the portfolio and starts a distributed
        // transaction, so it is a portfolio-level decision rather than a delivery one.
        var initiated = await client.PostAsync($"/projects/{projectId}/submit-for-initiation", null);
        initiated.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task APortfolioDirector_CanInitiateAProject()
    {
        var objectiveId = await AnObjectiveAsync();
        var client = factory.CreateClient().WithRole(Roles.PortfolioDirector);

        var created = await client.PostAsJsonAsync("/projects", AValidProject(objectiveId));
        var projectId = (await created.Content.ReadFromJsonAsync<ProjectBody>())!.Id;

        var initiated = await client.PostAsync($"/projects/{projectId}/submit-for-initiation", null);

        initiated.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthChecks_StayOpen()
    {
        var response = await factory.CreateClient().GetAsync("/health");

        // A liveness probe cannot present a token. A readiness endpoint that returns 401
        // makes an orchestrator kill a perfectly healthy pod.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private sealed record ObjectiveBody(Guid Id, string Code, string Title);

    private sealed record ProjectBody(Guid Id, string Code, string Stage);
}
