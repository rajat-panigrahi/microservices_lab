using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Api;
using StrategyOps.BuildingBlocks.Results;
using StrategyOps.Projects.Api.Infrastructure;

namespace StrategyOps.Projects.Api.Features.GetProject;

public sealed record ProjectDetail(
    Guid Id,
    string Code,
    string Name,
    Guid ObjectiveId,
    string ObjectiveTitle,
    string Sponsor,
    decimal Budget,
    string Stage,
    string Health,
    string? HealthReason,
    string? FailureReason,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ActivatedAtUtc,
    DateTimeOffset? ClosedAtUtc);

/// <summary>
/// A read slice. Note it projects straight to a DTO with <c>AsNoTracking</c> rather than
/// loading the aggregate - reads and writes have different shapes and different costs, which
/// is the same observation CQRS makes at a larger scale in the Reporting service.
/// </summary>
public sealed class GetProjectHandler(ProjectsDbContext db)
{
    public async Task<Result<ProjectDetail>> HandleAsync(Guid projectId, CancellationToken ct)
    {
        var detail = await db.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Join(
                db.Objectives.AsNoTracking(),
                p => p.ObjectiveId,
                o => o.Id,
                (p, o) => new ProjectDetail(
                    p.Id,
                    p.Code,
                    p.Name,
                    p.ObjectiveId,
                    o.Title,
                    p.Sponsor,
                    p.Budget,
                    p.Stage.ToString(),
                    p.Health.ToString(),
                    p.HealthReason,
                    p.FailureReason,
                    p.CreatedAtUtc,
                    p.ActivatedAtUtc,
                    p.ClosedAtUtc))
            .FirstOrDefaultAsync(ct);

        return detail is null
            ? Result<ProjectDetail>.NotFound("project.not_found", $"Project '{projectId}' does not exist.")
            : Result<ProjectDetail>.Ok(detail);
    }
}

public sealed class GetProjectEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/projects/{projectId:guid}", async (
                Guid projectId,
                GetProjectHandler handler,
                CancellationToken ct) => (await handler.HandleAsync(projectId, ct)).ToHttpResult())
            .WithName("GetProject")
            .WithSummary("Fetch one project")
            .WithTags("Projects")
            .Produces<ProjectDetail>()
            .ProducesProblem(StatusCodes.Status404NotFound);
}
