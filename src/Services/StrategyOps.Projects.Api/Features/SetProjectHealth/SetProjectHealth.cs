using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using StrategyOps.BuildingBlocks.Api;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.BuildingBlocks.Results;
using StrategyOps.Contracts.V1.Projects;
using StrategyOps.Projects.Api.Domain;
using StrategyOps.Projects.Api.Infrastructure;

namespace StrategyOps.Projects.Api.Features.SetProjectHealth;

public sealed record SetProjectHealthCommand(string Health, string Reason);

public sealed record SetProjectHealthResponse(Guid Id, string Health, bool Changed);

public sealed class SetProjectHealthValidator : AbstractValidator<SetProjectHealthCommand>
{
    public SetProjectHealthValidator()
    {
        RuleFor(x => x.Health)
            .NotEmpty()
            .Must(h => Enum.TryParse<ProjectHealth>(h, ignoreCase: true, out _))
            .WithMessage("Health must be one of: Green, Amber, Red.");
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

/// <summary>
/// Sets RAG status. Used by an operator directly, and by the risk-escalation choreography in
/// phase 2 - which is why the "did it actually change?" answer matters: a redelivered
/// escalation must not emit a second ProjectHealthChanged event.
/// </summary>
public sealed class SetProjectHealthHandler(ProjectsDbContext db, IOutboxWriter outbox)
{
    public async Task<Result<SetProjectHealthResponse>> HandleAsync(Guid projectId, SetProjectHealthCommand command, CancellationToken ct)
    {
        var project = await db.Projects.FindAsync([projectId], ct);

        if (project is null)
        {
            return Result<SetProjectHealthResponse>.NotFound("project.not_found", $"Project '{projectId}' does not exist.");
        }

        if (!Enum.TryParse<ProjectHealth>(command.Health, ignoreCase: true, out var health))
        {
            return Result<SetProjectHealthResponse>.Invalid("project.unknown_health", $"'{command.Health}' is not a valid health value.");
        }

        var changed = project.SetHealth(health, command.Reason);

        if (changed)
        {
            outbox.Enqueue(new ProjectHealthChanged
            {
                ProjectId = project.Id,
                Code = project.Code,
                Health = project.Health.ToString(),
                Reason = command.Reason
            });

            await db.SaveChangesAsync(ct);
        }

        return Result<SetProjectHealthResponse>.Ok(
            new SetProjectHealthResponse(project.Id, project.Health.ToString(), changed));
    }
}

public sealed class SetProjectHealthEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) =>
        app.MapPut("/projects/{projectId:guid}/health", async (
                Guid projectId,
                SetProjectHealthCommand command,
                SetProjectHealthHandler handler,
                CancellationToken ct) => (await handler.HandleAsync(projectId, command, ct)).ToHttpResult())
            .WithName("SetProjectHealth")
            .WithSummary("Move a project's RAG status")
            .WithTags("Projects")
            .WithValidation<SetProjectHealthCommand>()
            .Produces<SetProjectHealthResponse>();
}
