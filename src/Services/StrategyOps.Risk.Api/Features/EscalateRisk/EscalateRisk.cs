using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Api;
using StrategyOps.BuildingBlocks.Auth;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.BuildingBlocks.Results;
using StrategyOps.BuildingBlocks.Time;
using StrategyOps.Contracts.V1.Risks;
using StrategyOps.Risk.Api.Infrastructure;

namespace StrategyOps.Risk.Api.Features.EscalateRisk;

public sealed record EscalateRiskCommand(string Reason);

public sealed record EscalateRiskResponse(Guid Id, string Status);

public sealed class EscalateRiskValidator : AbstractValidator<EscalateRiskCommand>
{
    public EscalateRiskValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
}

/// <summary>
/// Declares that a risk has happened, and fires the choreographed chain.
/// </summary>
/// <remarks>
/// Nothing here knows that an issue will be raised, that the project's RAG status will drop,
/// or that a benefit will be flagged. It publishes one fact - "this risk materialised" - and
/// three other services decide independently what that means for them.
///
/// That is the trade choreography makes: adding a fourth reaction later needs no change to
/// this file, but no single place tells you what actually happens when a risk escalates.
/// Compare with the orchestrated saga in Projects, where one state machine says exactly what
/// happens and in what order.
/// </remarks>
public sealed class EscalateRiskHandler(RiskDbContext db, IOutboxWriter outbox, IClock clock)
{
    public async Task<Result<EscalateRiskResponse>> HandleAsync(Guid riskId, EscalateRiskCommand command, CancellationToken ct)
    {
        var risk = await db.Risks.FirstOrDefaultAsync(r => r.Id == riskId, ct);

        if (risk is null)
        {
            return Result<EscalateRiskResponse>.NotFound("risk.not_found", $"Risk '{riskId}' does not exist.");
        }

        var register = await db.Registers.FirstOrDefaultAsync(r => r.Id == risk.RegisterId, ct);

        if (register is null)
        {
            return Result<EscalateRiskResponse>.NotFound("risk.register_not_found", "The risk's register is missing.");
        }

        // Throws if the risk is already materialised, which is what stops a retried request
        // from starting the chain twice.
        risk.Escalate(command.Reason, clock.UtcNow);

        outbox.Enqueue(new RiskEscalated
        {
            RiskId = risk.Id,
            ProjectId = register.ProjectId,
            Title = risk.Title,
            Tier = risk.Tier.ToString(),
            Reason = command.Reason
        });

        await db.SaveChangesAsync(ct);

        return Result<EscalateRiskResponse>.Ok(new EscalateRiskResponse(risk.Id, risk.Status.ToString()));
    }
}

public sealed class EscalateRiskEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/risks/{riskId:guid}/escalate", async (
                Guid riskId,
                EscalateRiskCommand command,
                EscalateRiskHandler handler,
                CancellationToken ct) => (await handler.HandleAsync(riskId, command, ct)).ToHttpResult())
            .WithName("EscalateRisk")
            .WithSummary("Declare that a risk has materialised; raises an issue downstream")
            .WithTags("Risks")
            .RequireAuthorization(Policies.ManageRisk)
            .WithValidation<EscalateRiskCommand>()
            .Produces<EscalateRiskResponse>()
            .ProducesProblem(StatusCodes.Status409Conflict);
}
