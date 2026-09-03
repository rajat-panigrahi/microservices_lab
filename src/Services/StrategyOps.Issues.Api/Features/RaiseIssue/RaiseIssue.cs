using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using StrategyOps.BuildingBlocks.Api;
using StrategyOps.BuildingBlocks.Auth;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.BuildingBlocks.Results;
using StrategyOps.BuildingBlocks.Time;
using StrategyOps.Contracts.V1.Issues;
using StrategyOps.Issues.Api.Domain;
using StrategyOps.Issues.Api.Infrastructure;

namespace StrategyOps.Issues.Api.Features.RaiseIssue;

public sealed record RaiseIssueCommand(Guid ProjectId, string Title, string Severity);

public sealed record RaiseIssueResponse(Guid Id, string Severity, DateTimeOffset TargetResolutionUtc);

public sealed class RaiseIssueValidator : AbstractValidator<RaiseIssueCommand>
{
    public RaiseIssueValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Severity)
            .NotEmpty()
            .Must(s => Enum.TryParse<IssueSeverity>(s, ignoreCase: true, out _))
            .WithMessage("Severity must be one of: Low, Medium, High, Critical.");
    }
}

public sealed class RaiseIssueHandler(IssuesDbContext db, IOutboxWriter outbox, IClock clock)
{
    public async Task<Result<RaiseIssueResponse>> HandleAsync(RaiseIssueCommand command, CancellationToken ct)
    {
        if (!Enum.TryParse<IssueSeverity>(command.Severity, ignoreCase: true, out var severity))
        {
            return Result<RaiseIssueResponse>.Invalid("issue.unknown_severity", $"'{command.Severity}' is not a valid severity.");
        }

        var issue = Issue.Raise(command.ProjectId, command.Title, severity, clock.UtcNow);

        db.Issues.Add(issue);

        outbox.Enqueue(new IssueRaised
        {
            IssueId = issue.Id,
            ProjectId = issue.ProjectId,
            OriginRiskId = null,
            Title = issue.Title,
            Severity = issue.Severity.ToString()
        });

        await db.SaveChangesAsync(ct);

        return Result<RaiseIssueResponse>.Created(
            new RaiseIssueResponse(issue.Id, issue.Severity.ToString(), issue.TargetResolutionUtc));
    }
}

public sealed class RaiseIssueEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/issues", async (RaiseIssueCommand command, RaiseIssueHandler handler, CancellationToken ct) =>
            {
                var result = await handler.HandleAsync(command, ct);
                return result.ToHttpResult(result.Value is null ? null : $"/issues/{result.Value.Id}");
            })
            .WithName("RaiseIssue")
            .WithSummary("Raise an issue directly, without an originating risk")
            .WithTags("Issues")
            .RequireAuthorization(Policies.ManageRisk)
            .WithValidation<RaiseIssueCommand>()
            .Produces<RaiseIssueResponse>(StatusCodes.Status201Created);
}
