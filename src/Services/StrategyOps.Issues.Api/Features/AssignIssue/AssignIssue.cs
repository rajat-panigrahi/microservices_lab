using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Api;
using StrategyOps.BuildingBlocks.Auth;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.BuildingBlocks.Results;
using StrategyOps.Contracts.V1.Issues;
using StrategyOps.Issues.Api.Infrastructure;

namespace StrategyOps.Issues.Api.Features.AssignIssue;

public sealed record AssignIssueCommand(string Owner);

public sealed record AssignIssueResponse(Guid Id, string Status, string Owner);

public sealed class AssignIssueValidator : AbstractValidator<AssignIssueCommand>
{
    public AssignIssueValidator() => RuleFor(x => x.Owner).NotEmpty().MaximumLength(120);
}

public sealed class AssignIssueHandler(IssuesDbContext db, IOutboxWriter outbox)
{
    public async Task<Result<AssignIssueResponse>> HandleAsync(Guid issueId, AssignIssueCommand command, CancellationToken ct)
    {
        var issue = await db.Issues.FirstOrDefaultAsync(i => i.Id == issueId, ct);

        if (issue is null)
        {
            return Result<AssignIssueResponse>.NotFound("issue.not_found", $"Issue '{issueId}' does not exist.");
        }

        issue.Assign(command.Owner);

        outbox.Enqueue(new IssueAssigned { IssueId = issue.Id, ProjectId = issue.ProjectId, Owner = command.Owner });

        await db.SaveChangesAsync(ct);

        return Result<AssignIssueResponse>.Ok(new AssignIssueResponse(issue.Id, issue.Status.ToString(), command.Owner));
    }
}

public sealed class AssignIssueEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) =>
        app.MapPut("/issues/{issueId:guid}/owner", async (
                Guid issueId,
                AssignIssueCommand command,
                AssignIssueHandler handler,
                CancellationToken ct) => (await handler.HandleAsync(issueId, command, ct)).ToHttpResult())
            .WithName("AssignIssue")
            .WithSummary("Give an issue a named owner")
            .WithTags("Issues")
            .RequireAuthorization(Policies.ManageRisk)
            .WithValidation<AssignIssueCommand>()
            .Produces<AssignIssueResponse>();
}
