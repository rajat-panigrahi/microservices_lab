using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Api;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.BuildingBlocks.Results;
using StrategyOps.Contracts.V1.Risks;
using StrategyOps.Risk.Api.Infrastructure;

namespace StrategyOps.Risk.Api.Features.RescoreRisk;

public sealed record RescoreRiskCommand(int Probability, int Impact);

public sealed record RescoreRiskResponse(Guid Id, int Score, string Tier);

public sealed class RescoreRiskValidator : AbstractValidator<RescoreRiskCommand>
{
    public RescoreRiskValidator()
    {
        RuleFor(x => x.Probability).InclusiveBetween(Domain.Risk.MinScale, Domain.Risk.MaxScale);
        RuleFor(x => x.Impact).InclusiveBetween(Domain.Risk.MinScale, Domain.Risk.MaxScale);
    }
}

public sealed class RescoreRiskHandler(RiskDbContext db, IOutboxWriter outbox)
{
    public async Task<Result<RescoreRiskResponse>> HandleAsync(Guid riskId, RescoreRiskCommand command, CancellationToken ct)
    {
        var risk = await db.Risks.FirstOrDefaultAsync(r => r.Id == riskId, ct);

        if (risk is null)
        {
            return Result<RescoreRiskResponse>.NotFound("risk.not_found", $"Risk '{riskId}' does not exist.");
        }

        var register = await db.Registers.FirstOrDefaultAsync(r => r.Id == risk.RegisterId, ct);

        if (register is null)
        {
            return Result<RescoreRiskResponse>.NotFound("risk.register_not_found", "The risk's register is missing.");
        }

        risk.Rescore(command.Probability, command.Impact);

        outbox.Enqueue(new RiskRescored
        {
            RiskId = risk.Id,
            ProjectId = register.ProjectId,
            Score = risk.Score,
            Tier = risk.Tier.ToString()
        });

        await db.SaveChangesAsync(ct);

        return Result<RescoreRiskResponse>.Ok(new RescoreRiskResponse(risk.Id, risk.Score, risk.Tier.ToString()));
    }
}

public sealed class RescoreRiskEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) =>
        app.MapPut("/risks/{riskId:guid}/score", async (
                Guid riskId,
                RescoreRiskCommand command,
                RescoreRiskHandler handler,
                CancellationToken ct) => (await handler.HandleAsync(riskId, command, ct)).ToHttpResult())
            .WithName("RescoreRisk")
            .WithSummary("Re-score a risk on the probability/impact matrix")
            .WithTags("Risks")
            .WithValidation<RescoreRiskCommand>()
            .Produces<RescoreRiskResponse>();
}
