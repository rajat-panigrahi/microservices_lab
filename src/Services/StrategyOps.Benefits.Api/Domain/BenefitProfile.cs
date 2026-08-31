using StrategyOps.BuildingBlocks.Domain;

namespace StrategyOps.Benefits.Api.Domain;

/// <summary>
/// What a project claims it will be worth, and what it has actually delivered so far.
/// </summary>
public sealed class BenefitProfile
{
    private BenefitProfile()
    {
    }

    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    public string ProjectCode { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public BenefitType Type { get; private set; }

    public decimal ForecastValue { get; private set; }

    public decimal RealisedToDate { get; private set; }

    public BenefitStatus Status { get; private set; }

    public string? AtRiskReason { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>
    /// How much of the forecast has actually landed. Capped at nothing - a project that
    /// over-delivers should show it rather than being flattened to 100%.
    /// </summary>
    public decimal RealisationPercent =>
        ForecastValue == 0 ? 0 : Math.Round(RealisedToDate / ForecastValue * 100m, 2);

    public static BenefitProfile Register(
        Guid projectId,
        string projectCode,
        string name,
        BenefitType type,
        decimal forecastValue,
        DateTimeOffset now) => new()
        {
            Id = Guid.NewGuid(),
            ProjectId = Guard.AgainstEmpty(projectId, "benefit.project_required", "A benefit profile belongs to a project."),
            ProjectCode = Guard.AgainstBlank(projectCode, "benefit.project_code_required", "A benefit profile needs the project code."),
            Name = Guard.AgainstBlank(name, "benefit.name_required", "A benefit profile needs a name."),
            Type = type,
            ForecastValue = Guard.AgainstNonPositive(forecastValue, "benefit.forecast_must_be_positive", "A benefit forecast must be greater than zero."),
            RealisedToDate = 0m,
            Status = BenefitStatus.Registered,
            CreatedAtUtc = now
        };

    public void Realise(decimal actualValue)
    {
        Guard.Against(
            Status == BenefitStatus.Closed,
            "benefit.closed",
            "A closed benefit profile cannot record further realisation.");

        Guard.AgainstNonPositive(actualValue, "benefit.realisation_must_be_positive", "A realisation must be greater than zero.");

        RealisedToDate += actualValue;

        // Recording value is evidence the benefit is landing, so it stops being at risk.
        Status = BenefitStatus.Realising;
        AtRiskReason = null;
    }

    /// <summary>
    /// Flags the forecast as in doubt. Returns false if it was already at risk, so a second
    /// signal does not publish a second event.
    /// </summary>
    public bool FlagAtRisk(string reason)
    {
        if (Status is BenefitStatus.Closed or BenefitStatus.AtRisk)
        {
            return false;
        }

        Status = BenefitStatus.AtRisk;
        AtRiskReason = Guard.AgainstBlank(reason, "benefit.at_risk_reason_required", "Flagging a benefit at risk needs a reason.");
        return true;
    }

    public void Close() => Status = BenefitStatus.Closed;
}
