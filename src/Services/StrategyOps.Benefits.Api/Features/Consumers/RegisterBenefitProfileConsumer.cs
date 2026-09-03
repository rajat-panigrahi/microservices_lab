using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StrategyOps.Benefits.Api.Domain;
using StrategyOps.Benefits.Api.Infrastructure;
using StrategyOps.BuildingBlocks.Domain;
using StrategyOps.BuildingBlocks.Inbox;
using StrategyOps.BuildingBlocks.Messaging;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.BuildingBlocks.Time;
using StrategyOps.Contracts.V1;
using StrategyOps.Contracts.V1.Benefits;
using StrategyOps.Contracts.V1.Sagas;

namespace StrategyOps.Benefits.Api.Features.Consumers;

/// <summary>
/// The Benefits service's leg of project initiation - and the one that can legitimately say no.
/// </summary>
/// <remarks>
/// <para>
/// A forecast above the portfolio ceiling needs a separate business case, so this service
/// rejects the project. By then KPI and Risk have very likely already succeeded, which is
/// exactly the situation the saga exists for: their work has to be undone, as real
/// operations, because there is no shared transaction to roll back.
/// </para>
/// <para>
/// Note that the rejection is published as an <b>event</b> rather than thrown as an
/// exception. An exception would be retried five times and then dead-lettered, and the saga
/// would sit waiting for an answer that never comes until its timeout fired. A business
/// refusal is not a transient fault, and treating it as one is a common and expensive
/// mistake.
/// </para>
/// </remarks>
public sealed class RegisterBenefitProfileConsumer(
    BenefitsDbContext db,
    IInboxStore inbox,
    IOutboxWriter outbox,
    IOptions<PortfolioBenefitPolicy> policy,
    IClock clock,
    ILogger<RegisterBenefitProfileConsumer> logger)
    : IdempotentConsumer<BenefitsDbContext, RegisterBenefitProfile>(db, inbox, logger)
{
    protected override async Task ConsumeOnceAsync(ConsumeContext<RegisterBenefitProfile> context)
    {
        var message = context.Message;

        var existing = await Db.Profiles
            .FirstOrDefaultAsync(p => p.ProjectId == message.ProjectId, context.CancellationToken);

        if (existing is not null)
        {
            Enqueue(new BenefitProfileRegistered
            {
                ProjectId = message.ProjectId,
                ProfileId = existing.Id,
                ProjectCode = message.ProjectCode,
                ForecastValue = existing.ForecastValue,
                CorrelationId = message.CorrelationId
            });

            return;
        }

        var forecast = policy.Value.ForecastFor(message.Budget);

        try
        {
            policy.Value.EnsureWithinCeiling(forecast);

            var profile = BenefitProfile.Register(
                message.ProjectId,
                message.ProjectCode,
                $"{message.ProjectName} benefits",
                BenefitType.Cashable,
                forecast,
                clock.UtcNow);

            Db.Profiles.Add(profile);

            Enqueue(new BenefitProfileRegistered
            {
                ProjectId = message.ProjectId,
                ProfileId = profile.Id,
                ProjectCode = message.ProjectCode,
                ForecastValue = forecast,
                CorrelationId = message.CorrelationId
            });

            Logger.LogInformation(
                "Registered a {Forecast:N0} benefit forecast for {ProjectCode}",
                forecast,
                message.ProjectCode);
        }
        catch (DomainException ex)
        {
            Logger.LogWarning(
                "Refused the benefit profile for {ProjectCode}: {Reason}",
                message.ProjectCode,
                ex.Message);

            Enqueue(new BenefitProfileRegistrationFailed
            {
                ProjectId = message.ProjectId,
                Reason = ex.Message,
                CorrelationId = message.CorrelationId
            });
        }

        void Enqueue(IntegrationEvent @event) => outbox.Enqueue(@event);
    }
}
