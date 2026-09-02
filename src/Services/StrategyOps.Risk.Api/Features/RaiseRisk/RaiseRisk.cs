using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using StrategyOps.BuildingBlocks.Api;
using StrategyOps.BuildingBlocks.Auth;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.BuildingBlocks.Results;
using StrategyOps.BuildingBlocks.Time;
using StrategyOps.Contracts.V1.Risks;
using StrategyOps.Risk.Api.Infrastructure;

namespace StrategyOps.Risk.Api.Features.RaiseRisk;

public sealed record RaiseRiskCommand(
    Guid ProjectId,
    string Title,
    string Category,
    int Probability,
    int Impact,
    string Owner);

public sealed record RaiseRiskResponse(Guid Id, int Score, string Tier);

public sealed class RaiseRiskValidator : AbstractValidator<RaiseRiskCommand>
{
    public RaiseRiskValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Category).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Owner).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Probability).InclusiveBetween(Domain.Risk.MinScale, Domain.Risk.MaxScale);
        RuleFor(x => x.Impact).InclusiveBetween(Domain.Risk.MinScale, Domain.Risk.MaxScale);
    }
}

public sealed class RaiseRiskHandler(RiskDbContext db, IOutboxWriter outbox, IClock clock)
{
    public async Task<Result<RaiseRiskResponse>> HandleAsync(RaiseRiskCommand command, CancellationToken ct)
    {
        var register = await db.ForProjectAsync(command.ProjectId, ct);

        // This service does not call the Projects service to check the project exists. It
        // knows about the project only because it was told - the register was provisioned by
        // an event. That is the whole point: no synchronous dependency on Projects to serve
        // this request, so Projects being down does not stop a risk being raised.
        if (register is null)
        {
            return Result<RaiseRiskResponse>.NotFound(
                "risk.register_not_found",
                $"Project '{command.ProjectId}' has no risk register; it may not have completed initiation yet.");
        }

        register.EnsureAcceptingRisks();

        var risk = Domain.Risk.Raise(
            register.Id,
            command.Title,
            command.Category,
            command.Probability,
            command.Impact,
            command.Owner,
            clock.UtcNow);

        db.Risks.Add(risk);

        outbox.Enqueue(new RiskRaised
        {
            RiskId = risk.Id,
            ProjectId = register.ProjectId,
            Title = risk.Title,
            Score = risk.Score,
            Tier = risk.Tier.ToString()
        });

        await db.SaveChangesAsync(ct);

        return Result<RaiseRiskResponse>.Created(new RaiseRiskResponse(risk.Id, risk.Score, risk.Tier.ToString()));
    }
}

public sealed class RaiseRiskEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/risks", async (RaiseRiskCommand command, RaiseRiskHandler handler, CancellationToken ct) =>
            {
                var result = await handler.HandleAsync(command, ct);
                return result.ToHttpResult(result.Value is null ? null : $"/risks/{result.Value.Id}");
            })
            .WithName("RaiseRisk")
            .WithSummary("Add a scored risk to a project's register")
            .WithTags("Risks")
            .RequireAuthorization(Policies.ManageRisk)
            .WithValidation<RaiseRiskCommand>()
            .Produces<RaiseRiskResponse>(StatusCodes.Status201Created);
}
