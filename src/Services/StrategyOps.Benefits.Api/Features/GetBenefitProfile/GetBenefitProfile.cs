using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using StrategyOps.Benefits.Api.Infrastructure;
using StrategyOps.BuildingBlocks.Api;
using StrategyOps.BuildingBlocks.Auth;
using StrategyOps.BuildingBlocks.Results;

namespace StrategyOps.Benefits.Api.Features.GetBenefitProfile;

public sealed record RealisationView(DateTimeOffset PeriodEndUtc, decimal ActualValue);

public sealed record BenefitProfileView(
    Guid ProfileId,
    Guid ProjectId,
    string ProjectCode,
    string Name,
    string Type,
    decimal ForecastValue,
    decimal RealisedToDate,
    decimal RealisationPercent,
    string Status,
    string? AtRiskReason,
    IReadOnlyList<RealisationView> Realisations);

public sealed class GetBenefitProfileHandler(BenefitsDbContext db)
{
    public async Task<Result<BenefitProfileView>> HandleAsync(Guid projectId, CancellationToken ct)
    {
        var profile = await db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.ProjectId == projectId, ct);

        if (profile is null)
        {
            return Result<BenefitProfileView>.NotFound("benefit.profile_not_found", $"Project '{projectId}' has no benefit profile.");
        }

        var realisations = await db.Realisations
            .AsNoTracking()
            .Where(r => r.ProfileId == profile.Id)
            .OrderBy(r => r.PeriodEndUtc)
            .Select(r => new RealisationView(r.PeriodEndUtc, r.ActualValue))
            .ToListAsync(ct);

        return Result<BenefitProfileView>.Ok(new BenefitProfileView(
            profile.Id,
            profile.ProjectId,
            profile.ProjectCode,
            profile.Name,
            profile.Type.ToString(),
            profile.ForecastValue,
            profile.RealisedToDate,
            profile.RealisationPercent,
            profile.Status.ToString(),
            profile.AtRiskReason,
            realisations));
    }
}

public sealed class GetBenefitProfileEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/projects/{projectId:guid}/benefits", async (
                Guid projectId,
                GetBenefitProfileHandler handler,
                CancellationToken ct) => (await handler.HandleAsync(projectId, ct)).ToHttpResult())
            .WithName("GetBenefitProfile")
            .WithSummary("A project's benefit forecast and what has actually been realised")
            .WithTags("Benefits")
            .RequireAuthorization(Policies.Read)
            .Produces<BenefitProfileView>()
            .ProducesProblem(StatusCodes.Status404NotFound);
}
