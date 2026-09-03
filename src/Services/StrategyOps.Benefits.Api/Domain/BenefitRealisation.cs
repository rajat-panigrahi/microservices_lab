namespace StrategyOps.Benefits.Api.Domain;

/// <summary>One period's actual, kept so the realisation curve can be shown over time.</summary>
public sealed class BenefitRealisation
{
    private BenefitRealisation()
    {
    }

    public Guid Id { get; private set; }

    public Guid ProfileId { get; private set; }

    public DateTimeOffset PeriodEndUtc { get; private set; }

    public decimal ActualValue { get; private set; }

    public static BenefitRealisation Record(Guid profileId, DateTimeOffset periodEndUtc, decimal actualValue) => new()
    {
        Id = Guid.NewGuid(),
        ProfileId = profileId,
        PeriodEndUtc = periodEndUtc,
        ActualValue = actualValue
    };
}
