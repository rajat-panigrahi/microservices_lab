using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Api;
using StrategyOps.BuildingBlocks.Auth;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.BuildingBlocks.Results;
using StrategyOps.Contracts.V1.Kpis;
using StrategyOps.Kpi.Api.Domain;
using StrategyOps.Kpi.Api.Infrastructure;

namespace StrategyOps.Kpi.Api.Features.RecordMeasurement;

public sealed record RecordMeasurementCommand(decimal Value, DateTimeOffset PeriodEndUtc, string RecordedBy);

public sealed record RecordMeasurementResponse(Guid KpiId, decimal Value, string Rag, bool Breached, bool Recovered);

public sealed class RecordMeasurementValidator : AbstractValidator<RecordMeasurementCommand>
{
    public RecordMeasurementValidator()
    {
        RuleFor(x => x.RecordedBy).NotEmpty().MaximumLength(120);
        RuleFor(x => x.PeriodEndUtc).NotEmpty();
    }
}

/// <summary>
/// Records a reading and, only when the RAG status actually moves, publishes a breach or a
/// recovery.
/// </summary>
/// <remarks>
/// Publishing on every measurement instead would be far easier and much worse: Benefits
/// subscribes to KpiBreached and flags the forecast at risk, so a monthly "still red"
/// reading would re-flag a benefit that everyone already knows about. Events should describe
/// transitions, not restate the current state.
/// </remarks>
public sealed class RecordMeasurementHandler(KpiDbContext db, IOutboxWriter outbox)
{
    public async Task<Result<RecordMeasurementResponse>> HandleAsync(Guid kpiId, RecordMeasurementCommand command, CancellationToken ct)
    {
        var kpi = await db.Kpis.FirstOrDefaultAsync(k => k.Id == kpiId, ct);

        if (kpi is null)
        {
            return Result<RecordMeasurementResponse>.NotFound("kpi.not_found", $"KPI '{kpiId}' does not exist.");
        }

        var scorecard = await db.Scorecards.FirstOrDefaultAsync(s => s.Id == kpi.ScorecardId, ct);

        if (scorecard is null)
        {
            return Result<RecordMeasurementResponse>.NotFound("kpi.scorecard_not_found", "The KPI's scorecard is missing.");
        }

        scorecard.EnsureAcceptingMeasurements();

        var previousRag = kpi.Record(command.Value, command.PeriodEndUtc);

        db.Measurements.Add(KpiMeasurement.Record(kpi.Id, command.PeriodEndUtc, command.Value, command.RecordedBy));

        outbox.Enqueue(new KpiMeasurementRecorded
        {
            KpiId = kpi.Id,
            ProjectId = scorecard.ProjectId,
            KpiName = kpi.Name,
            Value = command.Value,
            Rag = kpi.Rag.ToString()
        });

        var breached = kpi.Rag is KpiRag.Amber or KpiRag.Red && previousRag is not (KpiRag.Amber or KpiRag.Red);
        var recovered = kpi.Rag == KpiRag.Green && previousRag is KpiRag.Amber or KpiRag.Red;

        if (breached)
        {
            outbox.Enqueue(new KpiBreached
            {
                KpiId = kpi.Id,
                ProjectId = scorecard.ProjectId,
                KpiName = kpi.Name,
                Rag = kpi.Rag.ToString(),
                Value = command.Value,
                Target = kpi.Target
            });
        }
        else if (recovered)
        {
            outbox.Enqueue(new KpiRecovered
            {
                KpiId = kpi.Id,
                ProjectId = scorecard.ProjectId,
                KpiName = kpi.Name
            });
        }

        await db.SaveChangesAsync(ct);

        return Result<RecordMeasurementResponse>.Ok(
            new RecordMeasurementResponse(kpi.Id, command.Value, kpi.Rag.ToString(), breached, recovered));
    }
}

public sealed class RecordMeasurementEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/kpis/{kpiId:guid}/measurements", async (
                Guid kpiId,
                RecordMeasurementCommand command,
                RecordMeasurementHandler handler,
                CancellationToken ct) => (await handler.HandleAsync(kpiId, command, ct)).ToHttpResult())
            .WithName("RecordKpiMeasurement")
            .WithSummary("Record a period reading; publishes a breach only when RAG actually moves")
            .WithTags("KPIs")
            .RequireAuthorization(Policies.ManageDelivery)
            .WithValidation<RecordMeasurementCommand>()
            .Produces<RecordMeasurementResponse>()
            .ProducesProblem(StatusCodes.Status409Conflict);
}
