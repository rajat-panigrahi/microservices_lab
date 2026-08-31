namespace StrategyOps.Kpi.Api.Domain;

/// <summary>One reading of one KPI for one period, kept so a trend can be shown.</summary>
public sealed class KpiMeasurement
{
    private KpiMeasurement()
    {
    }

    public Guid Id { get; private set; }

    public Guid KpiId { get; private set; }

    public DateTimeOffset PeriodEndUtc { get; private set; }

    public decimal Value { get; private set; }

    public string RecordedBy { get; private set; } = string.Empty;

    public static KpiMeasurement Record(Guid kpiId, DateTimeOffset periodEndUtc, decimal value, string recordedBy) => new()
    {
        Id = Guid.NewGuid(),
        KpiId = kpiId,
        PeriodEndUtc = periodEndUtc,
        Value = value,
        RecordedBy = recordedBy
    };
}
