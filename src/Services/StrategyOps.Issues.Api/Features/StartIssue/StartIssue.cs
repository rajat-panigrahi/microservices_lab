using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Api;
using StrategyOps.BuildingBlocks.Results;
using StrategyOps.Issues.Api.Infrastructure;

namespace StrategyOps.Issues.Api.Features.StartIssue;

public sealed record StartIssueResponse(Guid Id, string Status);

public sealed class StartIssueHandler(IssuesDbContext db)
{
    public async Task<Result<StartIssueResponse>> HandleAsync(Guid issueId, CancellationToken ct)
    {
        var issue = await db.Issues.FirstOrDefaultAsync(i => i.Id == issueId, ct);

        if (issue is null)
        {
            return Result<StartIssueResponse>.NotFound("issue.not_found", $"Issue '{issueId}' does not exist.");
        }

        issue.Start();
        await db.SaveChangesAsync(ct);

        return Result<StartIssueResponse>.Ok(new StartIssueResponse(issue.Id, issue.Status.ToString()));
    }
}

public sealed class StartIssueEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/issues/{issueId:guid}/start", async (
                Guid issueId,
                StartIssueHandler handler,
                CancellationToken ct) => (await handler.HandleAsync(issueId, ct)).ToHttpResult())
            .WithName("StartIssue")
            .WithSummary("Begin work on an assigned issue")
            .WithTags("Issues")
            .Produces<StartIssueResponse>()
            .ProducesProblem(StatusCodes.Status409Conflict);
}
