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

namespace StrategyOps.Projects.Api.Features.SubmitForInitiation;

public sealed record SubmitForInitiationResponse(Guid Id, string Stage);

/// <summary>
/// The start of the distributed transaction.
/// </summary>
/// <remarks>
/// This is where a monolith would open one database transaction and create the scorecard,
/// the risk register and the benefit profile inline. Across services that is not available,
/// so the project moves to Initiating and a <see cref="ProjectInitiationRequested"/> event
/// goes out; the saga (phase 3) drives the rest and compensates if any leg fails.
/// </remarks>
public sealed class SubmitForInitiationHandler(ProjectsDbContext db, IOutboxWriter outbox, IClock clock)
{
    public async Task<Result<SubmitForInitiationResponse>> HandleAsync(Guid projectId, CancellationToken ct)
    {
        var project = await db.Projects.FindAsync([projectId], ct);

        if (project is null)
        {
            return Result<SubmitForInitiationResponse>.NotFound("project.not_found", $"Project '{projectId}' does not exist.");
        }

        // Throws DomainException if the project is not in a stage that allows this;
        // DomainExceptionHandler turns that into a 409 with a stable code.
        project.SubmitForInitiation(clock.UtcNow);

        outbox.Enqueue(new ProjectInitiationRequested
        {
            ProjectId = project.Id,
            Code = project.Code,
            Name = project.Name,
            ObjectiveId = project.ObjectiveId,
            Budget = project.Budget
        });

        await db.SaveChangesAsync(ct);

        return Result<SubmitForInitiationResponse>.Ok(new SubmitForInitiationResponse(project.Id, project.Stage.ToString()));
    }
}

public sealed class SubmitForInitiationEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/projects/{projectId:guid}/submit-for-initiation", async (
                Guid projectId,
                SubmitForInitiationHandler handler,
                CancellationToken ct) => (await handler.HandleAsync(projectId, ct)).ToHttpResult())
            .WithName("SubmitProjectForInitiation")
            .WithSummary("Kick off the initiation saga across KPI, Risk and Benefits")
            .WithTags("Projects")
            .RequireAuthorization(Policies.ManagePortfolio)
            .Produces<SubmitForInitiationResponse>()
            .ProducesProblem(StatusCodes.Status409Conflict);
}
