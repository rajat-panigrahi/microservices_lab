using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Api;
using StrategyOps.BuildingBlocks.Results;
using StrategyOps.Risk.Api.Infrastructure;

namespace StrategyOps.Risk.Api.Features.PlanMitigation;

public sealed record PlanMitigationCommand(string Plan);

public sealed record PlanMitigationResponse(Guid Id, string Status);

public sealed class PlanMitigationValidator : AbstractValidator<PlanMitigationCommand>
{
    public PlanMitigationValidator() => RuleFor(x => x.Plan).NotEmpty().MaximumLength(2000);
}

/// <summary>
/// Purely local: agreeing a mitigation plan changes nothing outside this service, so it
/// publishes no event. Not every state change is worth broadcasting - a service that
/// publishes an event per field update turns its internal model into everyone's problem.
/// </summary>
public sealed class PlanMitigationHandler(RiskDbContext db)
{
    public async Task<Result<PlanMitigationResponse>> HandleAsync(Guid riskId, PlanMitigationCommand command, CancellationToken ct)
    {
        var risk = await db.Risks.FirstOrDefaultAsync(r => r.Id == riskId, ct);

        if (risk is null)
        {
            return Result<PlanMitigationResponse>.NotFound("risk.not_found", $"Risk '{riskId}' does not exist.");
        }

        risk.PlanMitigation(command.Plan);
        await db.SaveChangesAsync(ct);

        return Result<PlanMitigationResponse>.Ok(new PlanMitigationResponse(risk.Id, risk.Status.ToString()));
    }
}

public sealed class PlanMitigationEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) =>
        app.MapPut("/risks/{riskId:guid}/mitigation", async (
                Guid riskId,
                PlanMitigationCommand command,
                PlanMitigationHandler handler,
                CancellationToken ct) => (await handler.HandleAsync(riskId, command, ct)).ToHttpResult())
            .WithName("PlanRiskMitigation")
            .WithSummary("Record the agreed mitigation plan for a risk")
            .WithTags("Risks")
            .WithValidation<PlanMitigationCommand>()
            .Produces<PlanMitigationResponse>();
}
