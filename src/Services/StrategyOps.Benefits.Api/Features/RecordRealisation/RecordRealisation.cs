using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using StrategyOps.Benefits.Api.Domain;
using StrategyOps.Benefits.Api.Infrastructure;
using StrategyOps.BuildingBlocks.Api;
using StrategyOps.BuildingBlocks.Auth;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.BuildingBlocks.Results;
using StrategyOps.Contracts.V1.Benefits;

namespace StrategyOps.Benefits.Api.Features.RecordRealisation;

public sealed record RecordRealisationCommand(decimal ActualValue, DateTimeOffset PeriodEndUtc);

public sealed record RecordRealisationResponse(Guid ProfileId, decimal RealisedToDate, decimal RealisationPercent, string Status);

public sealed class RecordRealisationValidator : AbstractValidator<RecordRealisationCommand>
{
    public RecordRealisationValidator()
    {
        RuleFor(x => x.ActualValue).GreaterThan(0);
        RuleFor(x => x.PeriodEndUtc).NotEmpty();
    }
}

public sealed class RecordRealisationHandler(BenefitsDbContext db, IOutboxWriter outbox)
{
    public async Task<Result<RecordRealisationResponse>> HandleAsync(Guid projectId, RecordRealisationCommand command, CancellationToken ct)
    {
        var profile = await db.Profiles.FirstOrDefaultAsync(p => p.ProjectId == projectId, ct);

        if (profile is null)
        {
            return Result<RecordRealisationResponse>.NotFound("benefit.profile_not_found", $"Project '{projectId}' has no benefit profile.");
        }

        profile.Realise(command.ActualValue);
        db.Realisations.Add(BenefitRealisation.Record(profile.Id, command.PeriodEndUtc, command.ActualValue));

        outbox.Enqueue(new BenefitRealised
        {
            ProjectId = profile.ProjectId,
            ProfileId = profile.Id,
            ActualValue = command.ActualValue,
            RealisedToDate = profile.RealisedToDate,
            RealisationPercent = profile.RealisationPercent
        });

        await db.SaveChangesAsync(ct);

        return Result<RecordRealisationResponse>.Ok(new RecordRealisationResponse(
            profile.Id,
            profile.RealisedToDate,
            profile.RealisationPercent,
            profile.Status.ToString()));
    }
}

public sealed class RecordRealisationEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/projects/{projectId:guid}/benefits/realisations", async (
                Guid projectId,
                RecordRealisationCommand command,
                RecordRealisationHandler handler,
                CancellationToken ct) => (await handler.HandleAsync(projectId, command, ct)).ToHttpResult())
            .WithName("RecordBenefitRealisation")
            .WithSummary("Record value actually delivered against the forecast")
            .WithTags("Benefits")
            .RequireAuthorization(Policies.ManageDelivery)
            .WithValidation<RecordRealisationCommand>()
            .Produces<RecordRealisationResponse>()
            .ProducesProblem(StatusCodes.Status409Conflict);
}
