using System.Net;
using System.Net.Http.Json;
using Shouldly;
using StrategyOps.BuildingBlocks.Auth;

namespace StrategyOps.Slice.Tests.Issues;

/// <summary>
/// These exist because an end-to-end run found a 500 that every unit test had missed:
/// SQLite cannot ORDER BY a DateTimeOffset, and issues are listed by SLA deadline. The bug
/// was invisible until real SQL ran against a real provider - which is precisely the gap the
/// slice tier is supposed to close.
/// </summary>
[Collection(nameof(IssuesApiCollection))]
public class IssueQueryTests(IssuesApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient().WithRole(Roles.ProjectManager);

    private async Task<Guid> AnIssueAsync(Guid projectId, string title, string severity)
    {
        var response = await _client.PostAsJsonAsync("/issues", new { ProjectId = projectId, Title = title, Severity = severity });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<RaisedBody>())!.Id;
    }

    [Fact]
    public async Task ListingIssues_SortsByDeadlineAndDoesNotBlowUpOnTheDateColumn()
    {
        var projectId = Guid.NewGuid();

        // Deliberately created worst-last: Low has a 20-day SLA, Critical a 2-day one, so
        // correct ordering has to come from the database rather than insertion order.
        await AnIssueAsync(projectId, "Low severity, distant deadline", "Low");
        await AnIssueAsync(projectId, "Critical severity, urgent deadline", "Critical");
        await AnIssueAsync(projectId, "Medium severity", "Medium");

        var response = await _client.GetAsync($"/issues?projectId={projectId}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var issues = await response.Content.ReadFromJsonAsync<List<SummaryBody>>();
        issues!.Select(i => i.Severity).ShouldBe(["Critical", "Medium", "Low"]);
    }

    [Fact]
    public async Task ListingIssues_FiltersByStatus()
    {
        var projectId = Guid.NewGuid();
        var issueId = await AnIssueAsync(projectId, "Needs an owner", "High");

        await _client.PutAsJsonAsync($"/issues/{issueId}/owner", new { Owner = "I. Owner" });

        var assigned = await _client.GetFromJsonAsync<List<SummaryBody>>($"/issues?projectId={projectId}&status=Assigned");
        var brandNew = await _client.GetFromJsonAsync<List<SummaryBody>>($"/issues?projectId={projectId}&status=New");

        assigned!.ShouldHaveSingleItem().Id.ShouldBe(issueId);
        brandNew!.ShouldBeEmpty();
    }

    [Fact]
    public async Task ListingIssues_WithAnUnknownStatus_Is400()
    {
        var response = await _client.GetAsync("/issues?status=Imaginary");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task FetchingAnIssue_ReportsWhetherItHasBreachedItsSla()
    {
        var projectId = Guid.NewGuid();
        var issueId = await AnIssueAsync(projectId, "Fresh critical issue", "Critical");

        var detail = await _client.GetFromJsonAsync<DetailBody>($"/issues/{issueId}");

        detail!.BreachedSla.ShouldBeFalse();
        detail.TargetResolutionUtc.ShouldBeGreaterThan(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task ResolvingAnUnassignedIssue_Is409()
    {
        var issueId = await AnIssueAsync(Guid.NewGuid(), "Nobody owns this", "Medium");

        var response = await _client.PostAsJsonAsync($"/issues/{issueId}/resolve", new { Notes = "cannot resolve" });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain("issue.invalid_status_transition");
    }

    private sealed record RaisedBody(Guid Id, string Severity, DateTimeOffset TargetResolutionUtc);

    private sealed record SummaryBody(Guid Id, Guid ProjectId, Guid? OriginRiskId, string Title, string Severity, string Status, string? Owner, DateTimeOffset TargetResolutionUtc);

    private sealed record DetailBody(Guid Id, string Severity, string Status, DateTimeOffset TargetResolutionUtc, bool BreachedSla);
}
