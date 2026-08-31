using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Api;
using StrategyOps.BuildingBlocks.Results;
using StrategyOps.Issues.Api.Domain;
using StrategyOps.Issues.Api.Infrastructure;

namespace StrategyOps.Issues.Api.Features.ListIssues;

public sealed record IssueSummary(
    Guid Id,
    Guid ProjectId,
    Guid? OriginRiskId,
    string Title,
    string Severity,
    string Status,
    string? Owner,
    DateTimeOffset TargetResolutionUtc);

public sealed class ListIssuesHandler(IssuesDbContext db)
{
    public async Task<Result<IReadOnlyList<IssueSummary>>> HandleAsync(Guid? projectId, string? status, CancellationToken ct)
    {
        var query = db.Issues.AsNoTracking();

        if (projectId is not null)
        {
            query = query.Where(i => i.ProjectId == projectId);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<IssueStatus>(status, ignoreCase: true, out var parsed))
            {
                return Result<IReadOnlyList<IssueSummary>>.Invalid("issue.unknown_status", $"'{status}' is not a valid issue status.");
            }

            query = query.Where(i => i.Status == parsed);
        }

        var items = await query
            .OrderBy(i => i.TargetResolutionUtc)
            .Select(i => new IssueSummary(
                i.Id,
                i.ProjectId,
                i.OriginRiskId,
                i.Title,
                i.Severity.ToString(),
                i.Status.ToString(),
                i.Owner,
                i.TargetResolutionUtc))
            .ToListAsync(ct);

        return Result<IReadOnlyList<IssueSummary>>.Ok(items);
    }
}

public sealed class ListIssuesEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/issues", async (
                Guid? projectId,
                string? status,
                ListIssuesHandler handler,
                CancellationToken ct) => (await handler.HandleAsync(projectId, status, ct)).ToHttpResult())
            .WithName("ListIssues")
            .WithSummary("List issues, most urgent deadline first")
            .WithTags("Issues")
            .Produces<IReadOnlyList<IssueSummary>>();
}
