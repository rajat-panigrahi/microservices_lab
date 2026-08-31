using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Api;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.BuildingBlocks.Results;
using StrategyOps.BuildingBlocks.Time;
using StrategyOps.Contracts.V1.Issues;
using StrategyOps.Issues.Api.Infrastructure;

namespace StrategyOps.Issues.Api.Features.ResolveIssue;

public sealed record ResolveIssueCommand(string Notes);

public sealed record ResolveIssueResponse(Guid Id, string Status);

public sealed class ResolveIssueValidator : AbstractValidator<ResolveIssueCommand>
{
    public ResolveIssueValidator() => RuleFor(x => x.Notes).NotEmpty().MaximumLength(2000);
}

/// <summary>
/// Resolving an issue publishes IssueResolved, which the Risk service uses to close the
/// originating risk - the return leg of the choreography.
/// </summary>
public sealed class ResolveIssueHandler(IssuesDbContext db, IOutboxWriter outbox, IClock clock)
{
    public async Task<Result<ResolveIssueResponse>> HandleAsync(Guid issueId, ResolveIssueCommand command, CancellationToken ct)
    {
        var issue = await db.Issues.FirstOrDefaultAsync(i => i.Id == issueId, ct);

        if (issue is null)
        {
            return Result<ResolveIssueResponse>.NotFound("issue.not_found", $"Issue '{issueId}' does not exist.");
        }

        issue.Resolve(command.Notes, clock.UtcNow);

        outbox.Enqueue(new IssueResolved
        {
            IssueId = issue.Id,
            ProjectId = issue.ProjectId,
            OriginRiskId = issue.OriginRiskId
        });

        await db.SaveChangesAsync(ct);

        return Result<ResolveIssueResponse>.Ok(new ResolveIssueResponse(issue.Id, issue.Status.ToString()));
    }
}

public sealed class ResolveIssueEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/issues/{issueId:guid}/resolve", async (
                Guid issueId,
                ResolveIssueCommand command,
                ResolveIssueHandler handler,
                CancellationToken ct) => (await handler.HandleAsync(issueId, command, ct)).ToHttpResult())
            .WithName("ResolveIssue")
            .WithSummary("Resolve an issue; closes the originating risk if there was one")
            .WithTags("Issues")
            .WithValidation<ResolveIssueCommand>()
            .Produces<ResolveIssueResponse>()
            .ProducesProblem(StatusCodes.Status409Conflict);
}
