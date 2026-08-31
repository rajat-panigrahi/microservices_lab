using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using StrategyOps.BuildingBlocks.Api;
using StrategyOps.BuildingBlocks.Results;
using StrategyOps.Projects.Api.Domain;
using StrategyOps.Projects.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace StrategyOps.Projects.Api.Features.CreateObjective;

public sealed record CreateObjectiveCommand(string Code, string Title, string Horizon, string Owner);

public sealed record CreateObjectiveResponse(Guid Id, string Code, string Title);

public sealed class CreateObjectiveValidator : AbstractValidator<CreateObjectiveCommand>
{
    public CreateObjectiveValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Horizon).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Owner).NotEmpty().MaximumLength(120);
    }
}

public sealed class CreateObjectiveHandler(ProjectsDbContext db)
{
    public async Task<Result<CreateObjectiveResponse>> HandleAsync(CreateObjectiveCommand command, CancellationToken ct)
    {
        var code = command.Code.Trim().ToUpperInvariant();

        if (await db.Objectives.AnyAsync(o => o.Code == code, ct))
        {
            return Result<CreateObjectiveResponse>.Conflict("objective.duplicate_code", $"Objective '{code}' already exists.");
        }

        var objective = StrategicObjective.Create(command.Code, command.Title, command.Horizon, command.Owner);

        db.Objectives.Add(objective);
        await db.SaveChangesAsync(ct);

        return Result<CreateObjectiveResponse>.Created(new CreateObjectiveResponse(objective.Id, objective.Code, objective.Title));
    }
}

public sealed class CreateObjectiveEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/objectives", async (
                CreateObjectiveCommand command,
                CreateObjectiveHandler handler,
                CancellationToken ct) =>
            {
                var result = await handler.HandleAsync(command, ct);
                return result.ToHttpResult(result.Value is null ? null : $"/objectives/{result.Value.Id}");
            })
            .WithName("CreateObjective")
            .WithSummary("Register a strategic objective for projects to deliver against")
            .WithTags("Objectives")
            .WithValidation<CreateObjectiveCommand>()
            .Produces<CreateObjectiveResponse>(StatusCodes.Status201Created);
}
