using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Api;
using StrategyOps.BuildingBlocks.Auth;
using StrategyOps.BuildingBlocks.Results;
using StrategyOps.Projects.Api.Domain;
using StrategyOps.Projects.Api.Infrastructure;

namespace StrategyOps.Projects.Api.Features.ListProjects;

public sealed record ProjectSummary(Guid Id, string Code, string Name, string Stage, string Health, decimal Budget);

public sealed record ProjectPage(IReadOnlyList<ProjectSummary> Items, int Page, int PageSize, int TotalCount);

public sealed class ListProjectsHandler(ProjectsDbContext db)
{
    public const int MaxPageSize = 100;

    public async Task<Result<ProjectPage>> HandleAsync(string? stage, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = db.Projects.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(stage))
        {
            if (!Enum.TryParse<ProjectStage>(stage, ignoreCase: true, out var parsed))
            {
                return Result<ProjectPage>.Invalid("project.unknown_stage", $"'{stage}' is not a valid project stage.");
            }

            query = query.Where(p => p.Stage == parsed);
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(p => p.Code)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new ProjectSummary(p.Id, p.Code, p.Name, p.Stage.ToString(), p.Health.ToString(), p.Budget))
            .ToListAsync(ct);

        return Result<ProjectPage>.Ok(new ProjectPage(items, page, pageSize, total));
    }
}

public sealed class ListProjectsEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/projects", async (
                string? stage,
                int? page,
                int? pageSize,
                ListProjectsHandler handler,
                CancellationToken ct) => (await handler.HandleAsync(stage, page ?? 1, pageSize ?? 20, ct)).ToHttpResult())
            .WithName("ListProjects")
            .WithSummary("List projects, optionally filtered by stage")
            .WithTags("Projects")
            .RequireAuthorization(Policies.Read)
            .Produces<ProjectPage>();
}
