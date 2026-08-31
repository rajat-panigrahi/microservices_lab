using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Api;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.BuildingBlocks.Results;
using StrategyOps.BuildingBlocks.Time;
using StrategyOps.Contracts.V1.Projects;
using StrategyOps.Projects.Api.Domain;
using StrategyOps.Projects.Api.Infrastructure;

namespace StrategyOps.Projects.Api.Features.CreateProject;

public sealed record CreateProjectCommand(string Code, string Name, Guid ObjectiveId, string Sponsor, decimal Budget);

public sealed record CreateProjectResponse(Guid Id, string Code, string Stage);

public sealed class CreateProjectValidator : AbstractValidator<CreateProjectCommand>
{
    public CreateProjectValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ObjectiveId).NotEmpty();
        RuleFor(x => x.Sponsor).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Budget).GreaterThan(0);
    }
}

/// <summary>
/// Creates a project in Draft. Nothing downstream happens yet - a draft is a local decision.
/// The fan-out starts at <c>SubmitForInitiation</c>.
/// </summary>
public sealed class CreateProjectHandler(ProjectsDbContext db, IOutboxWriter outbox, IClock clock)
{
    public async Task<Result<CreateProjectResponse>> HandleAsync(CreateProjectCommand command, CancellationToken ct)
    {
        var code = command.Code.Trim().ToUpperInvariant();

        if (await db.Projects.AnyAsync(p => p.Code == code, ct))
        {
            return Result<CreateProjectResponse>.Conflict("project.duplicate_code", $"Project '{code}' already exists.");
        }

        if (!await db.Objectives.AnyAsync(o => o.Id == command.ObjectiveId, ct))
        {
            return Result<CreateProjectResponse>.NotFound("project.objective_not_found", $"Objective '{command.ObjectiveId}' does not exist.");
        }

        var project = Project.CreateDraft(
            command.Code,
            command.Name,
            command.ObjectiveId,
            command.Sponsor,
            command.Budget,
            clock.UtcNow);

        db.Projects.Add(project);

        outbox.Enqueue(new ProjectDraftCreated
        {
            ProjectId = project.Id,
            Code = project.Code,
            Name = project.Name,
            ObjectiveId = project.ObjectiveId,
            Sponsor = project.Sponsor,
            Budget = project.Budget
        });

        // One SaveChanges: the project row and the outbox row commit together or not at all.
        await db.SaveChangesAsync(ct);

        return Result<CreateProjectResponse>.Created(
            new CreateProjectResponse(project.Id, project.Code, project.Stage.ToString()));
    }
}

public sealed class CreateProjectEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/projects", async (
                CreateProjectCommand command,
                CreateProjectHandler handler,
                CancellationToken ct) =>
            {
                var result = await handler.HandleAsync(command, ct);
                return result.ToHttpResult(result.Value is null ? null : $"/projects/{result.Value.Id}");
            })
            .WithName("CreateProject")
            .WithSummary("Draft a new project against a strategic objective")
            .WithTags("Projects")
            .WithValidation<CreateProjectCommand>()
            .Produces<CreateProjectResponse>(StatusCodes.Status201Created);
}
