using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Api;
using StrategyOps.BuildingBlocks.Auth;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.BuildingBlocks.Results;
using StrategyOps.Contracts.V1.Risks;
using StrategyOps.Risk.Api.Infrastructure;

namespace StrategyOps.Risk.Api.Features.CloseRisk;

public sealed record CloseRiskCommand(string Resolution);

public sealed record CloseRiskResponse(Guid Id, string Status);

public sealed class CloseRiskValidator : AbstractValidator<CloseRiskCommand>
{
    public CloseRiskValidator() => RuleFor(x => x.Resolution).NotEmpty().MaximumLength(1000);
}

public sealed class CloseRiskHandler(RiskDbContext db, IOutboxWriter outbox)
{
    public async Task<Result<CloseRiskResponse>> HandleAsync(Guid riskId, CloseRiskCommand command, CancellationToken ct)
    {
        var risk = await db.Risks.FirstOrDefaultAsync(r => r.Id == riskId, ct);

        if (risk is null)
        {
            return Result<CloseRiskResponse>.NotFound("risk.not_found", $"Risk '{riskId}' does not exist.");
        }

        var register = await db.Registers.FirstOrDefaultAsync(r => r.Id == risk.RegisterId, ct);

        if (register is null)
        {
            return Result<CloseRiskResponse>.NotFound("risk.register_not_found", "The risk's register is missing.");
        }

        risk.Close(command.Resolution);

        outbox.Enqueue(new RiskClosed { RiskId = risk.Id, ProjectId = register.ProjectId });

        await db.SaveChangesAsync(ct);

        return Result<CloseRiskResponse>.Ok(new CloseRiskResponse(risk.Id, risk.Status.ToString()));
    }
}

public sealed class CloseRiskEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/risks/{riskId:guid}/close", async (
                Guid riskId,
                CloseRiskCommand command,
                CloseRiskHandler handler,
                CancellationToken ct) => (await handler.HandleAsync(riskId, command, ct)).ToHttpResult())
            .WithName("CloseRisk")
            .WithSummary("Retire a risk with a resolution note")
            .WithTags("Risks")
            .RequireAuthorization(Policies.ManageRisk)
            .WithValidation<CloseRiskCommand>()
            .Produces<CloseRiskResponse>();
}
