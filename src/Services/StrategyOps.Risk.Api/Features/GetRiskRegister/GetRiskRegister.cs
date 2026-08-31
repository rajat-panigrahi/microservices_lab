using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Api;
using StrategyOps.BuildingBlocks.Results;
using StrategyOps.Risk.Api.Domain;
using StrategyOps.Risk.Api.Infrastructure;

namespace StrategyOps.Risk.Api.Features.GetRiskRegister;

public sealed record RiskSummary(
    Guid Id,
    string Title,
    string Category,
    int Probability,
    int Impact,
    int Score,
    string Tier,
    string Status,
    string Owner);

public sealed record RiskRegisterView(
    Guid RegisterId,
    Guid ProjectId,
    string ProjectCode,
    string Status,
    int OpenCount,
    int CriticalOpenCount,
    IReadOnlyList<RiskSummary> Risks);

public sealed class GetRiskRegisterHandler(RiskDbContext db)
{
    public async Task<Result<RiskRegisterView>> HandleAsync(Guid projectId, CancellationToken ct)
    {
        var register = await db.Registers.AsNoTracking().FirstOrDefaultAsync(r => r.ProjectId == projectId, ct);

        if (register is null)
        {
            return Result<RiskRegisterView>.NotFound("risk.register_not_found", $"Project '{projectId}' has no risk register.");
        }

        var risks = await db.Risks
            .AsNoTracking()
            .Where(r => r.RegisterId == register.Id)
            .OrderByDescending(r => r.Score)
            .Select(r => new RiskSummary(
                r.Id,
                r.Title,
                r.Category,
                r.Probability,
                r.Impact,
                r.Score,
                r.Tier.ToString(),
                r.Status.ToString(),
                r.Owner))
            .ToListAsync(ct);

        var open = risks.Count(r => r.Status is nameof(RiskStatus.Open) or nameof(RiskStatus.Mitigating));
        var criticalOpen = risks.Count(r =>
            r.Status is nameof(RiskStatus.Open) or nameof(RiskStatus.Mitigating) && r.Tier == nameof(RiskTier.Critical));

        return Result<RiskRegisterView>.Ok(new RiskRegisterView(
            register.Id,
            register.ProjectId,
            register.ProjectCode,
            register.Status.ToString(),
            open,
            criticalOpen,
            risks));
    }
}

public sealed class GetRiskRegisterEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/projects/{projectId:guid}/risk-register", async (
                Guid projectId,
                GetRiskRegisterHandler handler,
                CancellationToken ct) => (await handler.HandleAsync(projectId, ct)).ToHttpResult())
            .WithName("GetRiskRegister")
            .WithSummary("The full risk register for a project, worst first")
            .WithTags("Risks")
            .Produces<RiskRegisterView>()
            .ProducesProblem(StatusCodes.Status404NotFound);
}
