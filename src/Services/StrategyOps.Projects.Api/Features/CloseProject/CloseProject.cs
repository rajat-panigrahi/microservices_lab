using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using StrategyOps.BuildingBlocks.Api;
using StrategyOps.BuildingBlocks.Auth;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.BuildingBlocks.Results;
using StrategyOps.BuildingBlocks.Time;
using StrategyOps.Contracts.V1.Projects;
using StrategyOps.Projects.Api.Infrastructure;

namespace StrategyOps.Projects.Api.Features.CloseProject;

public sealed record CloseProjectResponse(Guid Id, string Stage);

public sealed class CloseProjectHandler(ProjectsDbContext db, IOutboxWriter outbox, IClock clock)
{
    public async Task<Result<CloseProjectResponse>> HandleAsync(Guid projectId, CancellationToken ct)
    {
        var project = await db.Projects.FindAsync([projectId], ct);

        if (project is null)
        {
            return Result<CloseProjectResponse>.NotFound("project.not_found", $"Project '{projectId}' does not exist.");
        }

        project.Close(clock.UtcNow);

        // Downstream services close their own records off when they see this - the Projects
        // service does not reach into their databases to do it for them.
        outbox.Enqueue(new ProjectClosed { ProjectId = project.Id, Code = project.Code });

        await db.SaveChangesAsync(ct);

        return Result<CloseProjectResponse>.Ok(new CloseProjectResponse(project.Id, project.Stage.ToString()));
    }
}

public sealed class CloseProjectEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/projects/{projectId:guid}/close", async (
                Guid projectId,
                CloseProjectHandler handler,
                CancellationToken ct) => (await handler.HandleAsync(projectId, ct)).ToHttpResult())
            .WithName("CloseProject")
            .WithSummary("Close a delivered project")
            .WithTags("Projects")
            .RequireAuthorization(Policies.ManagePortfolio)
            .Produces<CloseProjectResponse>()
            .ProducesProblem(StatusCodes.Status409Conflict);
}
