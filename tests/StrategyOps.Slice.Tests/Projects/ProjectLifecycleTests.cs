using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using StrategyOps.Contracts.V1.Projects;
using StrategyOps.BuildingBlocks.Outbox;

namespace StrategyOps.Slice.Tests.Projects;

[Collection(nameof(ProjectsApiCollection))]
public class ProjectLifecycleTests(ProjectsApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<Guid> AnObjectiveAsync(string code)
    {
        var response = await _client.PostAsJsonAsync("/objectives", new
        {
            Code = code,
            Title = "Reduce operating cost by 15%",
            Horizon = "FY27",
            Owner = "COO"
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<ObjectiveBody>();
        return body!.Id;
    }

    private async Task<Guid> ADraftProjectAsync(string code)
    {
        var objectiveId = await AnObjectiveAsync($"SO-{code}");

        var response = await _client.PostAsJsonAsync("/projects", new
        {
            Code = code,
            Name = "Warehouse automation",
            ObjectiveId = objectiveId,
            Sponsor = "A. Sponsor",
            Budget = 250_000m
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<ProjectBody>();
        return body!.Id;
    }

    [Fact]
    public async Task CreatingAProject_PersistsItAsADraft()
    {
        var projectId = await ADraftProjectAsync("PRJ-100");

        var detail = await _client.GetFromJsonAsync<ProjectDetailBody>($"/projects/{projectId}");

        detail!.Stage.ShouldBe("Draft");
        detail.Health.ShouldBe("Green");
        detail.ObjectiveTitle.ShouldBe("Reduce operating cost by 15%");
    }

    [Fact]
    public async Task CreatingAProject_WritesExactlyOneOutboxMessageInTheSameTransaction()
    {
        var projectId = await ADraftProjectAsync("PRJ-101");

        var messages = await factory.QueryAsync(db => db.OutboxMessages
            .Where(m => m.Payload.Contains(projectId.ToString()))
            .ToListAsync());

        messages.Count.ShouldBe(1);
        messages[0].Type.ShouldBe(typeof(ProjectDraftCreated).FullName);
        messages[0].ProcessedAtUtc.ShouldBeNull("the message is queued, not yet published");
    }

    [Fact]
    public async Task DrainingTheOutbox_MarksMessagesProcessed()
    {
        var projectId = await ADraftProjectAsync("PRJ-102");

        await factory.DrainOutboxAsync();

        var pendingForThisProject = await factory.QueryAsync(db => db.OutboxMessages
            .CountAsync(m => m.ProcessedAtUtc == null && m.Payload.Contains(projectId.ToString())));

        pendingForThisProject.ShouldBe(0);
    }

    [Fact]
    public async Task CreatingAProject_AgainstAnUnknownObjective_Is404()
    {
        var response = await _client.PostAsJsonAsync("/projects", new
        {
            Code = "PRJ-103",
            Name = "Warehouse automation",
            ObjectiveId = Guid.NewGuid(),
            Sponsor = "A. Sponsor",
            Budget = 1000m
        });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreatingAProject_WithADuplicateCode_Is409()
    {
        await ADraftProjectAsync("PRJ-104");
        var objectiveId = await AnObjectiveAsync("SO-PRJ-104-B");

        var response = await _client.PostAsJsonAsync("/projects", new
        {
            Code = "PRJ-104",
            Name = "Something else",
            ObjectiveId = objectiveId,
            Sponsor = "A. Sponsor",
            Budget = 1000m
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreatingAProject_WithABlankName_Is400WithValidationDetail()
    {
        var objectiveId = await AnObjectiveAsync("SO-PRJ-105");

        var response = await _client.PostAsJsonAsync("/projects", new
        {
            Code = "PRJ-105",
            Name = "",
            ObjectiveId = objectiveId,
            Sponsor = "A. Sponsor",
            Budget = 1000m
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Name");
    }

    [Fact]
    public async Task SubmittingForInitiation_MovesToInitiatingAndQueuesTheSagaTrigger()
    {
        var projectId = await ADraftProjectAsync("PRJ-106");

        var response = await _client.PostAsync($"/projects/{projectId}/submit-for-initiation", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var detail = await _client.GetFromJsonAsync<ProjectDetailBody>($"/projects/{projectId}");
        detail!.Stage.ShouldBe("Initiating");

        var queued = await OutboxTypesForAsync(projectId);
        queued.ShouldContain(typeof(ProjectInitiationRequested).FullName!);
    }

    [Fact]
    public async Task SubmittingForInitiation_Twice_Is409FromTheAggregate()
    {
        var projectId = await ADraftProjectAsync("PRJ-107");
        await _client.PostAsync($"/projects/{projectId}/submit-for-initiation", null);

        var response = await _client.PostAsync($"/projects/{projectId}/submit-for-initiation", null);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain("project.invalid_stage_transition");
    }

    [Fact]
    public async Task SettingTheSameHealthTwice_PublishesOnlyOneEvent()
    {
        var projectId = await ADraftProjectAsync("PRJ-108");

        var first = await _client.PutAsJsonAsync($"/projects/{projectId}/health", new { Health = "Amber", Reason = "critical risk escalated" });
        var second = await _client.PutAsJsonAsync($"/projects/{projectId}/health", new { Health = "Amber", Reason = "same escalation redelivered" });

        (await first.Content.ReadFromJsonAsync<HealthBody>())!.Changed.ShouldBeTrue();
        (await second.Content.ReadFromJsonAsync<HealthBody>())!.Changed.ShouldBeFalse();

        var healthEvents = (await OutboxTypesForAsync(projectId))
            .Count(t => t == typeof(ProjectHealthChanged).FullName);

        healthEvents.ShouldBe(1, "a redelivered escalation must not fan out a second time");
    }

    [Fact]
    public async Task SettingAnUnknownHealthValue_Is400()
    {
        var projectId = await ADraftProjectAsync("PRJ-109");

        var response = await _client.PutAsJsonAsync($"/projects/{projectId}/health", new { Health = "Purple", Reason = "no such colour" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ClosingADraft_Is409()
    {
        var projectId = await ADraftProjectAsync("PRJ-110");

        var response = await _client.PostAsync($"/projects/{projectId}/close", null);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task GettingAnUnknownProject_Is404()
    {
        var response = await _client.GetAsync($"/projects/{Guid.NewGuid()}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListingProjects_FiltersByStage()
    {
        var projectId = await ADraftProjectAsync("PRJ-111");
        await _client.PostAsync($"/projects/{projectId}/submit-for-initiation", null);

        var page = await _client.GetFromJsonAsync<PageBody>("/projects?stage=Initiating&pageSize=100");

        page!.Items.ShouldContain(p => p.Id == projectId);
        page.Items.ShouldAllBe(p => p.Stage == "Initiating");
    }

    [Fact]
    public async Task ListingProjects_WithAnUnknownStage_Is400()
    {
        var response = await _client.GetAsync("/projects?stage=Imaginary");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task HealthEndpoint_ReportsHealthy()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task<List<string>> OutboxTypesForAsync(Guid projectId) =>
        await factory.QueryAsync(db => db.OutboxMessages
            .Where(m => m.Payload.Contains(projectId.ToString()))
            .Select(m => m.Type)
            .ToListAsync());

    private sealed record ObjectiveBody(Guid Id, string Code, string Title);

    private sealed record ProjectBody(Guid Id, string Code, string Stage);

    private sealed record ProjectDetailBody(Guid Id, string Code, string Stage, string Health, string ObjectiveTitle);

    private sealed record HealthBody(Guid Id, string Health, bool Changed);

    private sealed record PageBody(List<ProjectSummaryBody> Items, int Page, int PageSize, int TotalCount);

    private sealed record ProjectSummaryBody(Guid Id, string Code, string Name, string Stage, string Health, decimal Budget);
}
