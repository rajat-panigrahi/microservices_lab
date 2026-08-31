using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Api;
using StrategyOps.BuildingBlocks.Results;
using StrategyOps.BuildingBlocks.Time;
using StrategyOps.Issues.Api.Infrastructure;

namespace StrategyOps.Issues.Api.Features.GetIssue;

public sealed record IssueDetail(
    Guid Id,
    Guid ProjectId,
    Guid? OriginRiskId,
    string Title,
    string Severity,
    string Status,
    string? Owner,
    string? ResolutionNotes,
    DateTimeOffset RaisedAtUtc,
    DateTimeOffset TargetResolutionUtc,
    DateTimeOffset? ResolvedAtUtc,
    bool BreachedSla);

public sealed class GetIssueHandler(IssuesDbContext db, IClock clock)
{
    public async Task<Result<IssueDetail>> HandleAsync(Guid issueId, CancellationToken ct)
    {
        var issue = await db.Issues.AsNoTracking().FirstOrDefaultAsync(i => i.Id == issueId, ct);

        if (issue is null)
        {
            return Result<IssueDetail>.NotFound("issue.not_found", $"Issue '{issueId}' does not exist.");
        }

        return Result<IssueDetail>.Ok(new IssueDetail(
            issue.Id,
            issue.ProjectId,
            issue.OriginRiskId,
            issue.Title,
            issue.Severity.ToString(),
            issue.Status.ToString(),
            issue.Owner,
            issue.ResolutionNotes,
            issue.RaisedAtUtc,
            issue.TargetResolutionUtc,
            issue.ResolvedAtUtc,
            issue.HasBreachedSla(clock.UtcNow)));
    }
}

public sealed class GetIssueEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/issues/{issueId:guid}", async (
                Guid issueId,
                GetIssueHandler handler,
                CancellationToken ct) => (await handler.HandleAsync(issueId, ct)).ToHttpResult())
            .WithName("GetIssue")
            .WithSummary("Fetch one issue, including whether it has breached its SLA")
            .WithTags("Issues")
            .Produces<IssueDetail>()
            .ProducesProblem(StatusCodes.Status404NotFound);
}
