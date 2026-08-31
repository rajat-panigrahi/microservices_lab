using StrategyOps.BuildingBlocks.Domain;

namespace StrategyOps.Benefits.Api.Domain;

/// <summary>
/// The rule that can make project initiation fail, and therefore the rule that exercises the
/// saga's compensation path.
/// </summary>
/// <remarks>
/// <para>
/// A project's benefit forecast is derived from its budget. The portfolio has a ceiling on
/// how large a single project's claimed benefit may be without a separate business case -
/// a real control, and the kind of rule that genuinely does reject a project after other
/// services have already set things up for it.
/// </para>
/// <para>
/// This matters for the demo: to see compensation run, you do not stub a failure or kill a
/// process. You submit a project whose budget implies a forecast above the ceiling, and the
/// system legitimately refuses it - which is exactly the situation sagas exist for.
/// </para>
/// </remarks>
public sealed class PortfolioBenefitPolicy
{
    public const string SectionName = "Benefits";

    /// <summary>Forecast benefit as a proportion of project budget.</summary>
    public decimal ForecastMultiplier { get; set; } = 1.4m;

    /// <summary>Largest forecast a single project may claim without a separate business case.</summary>
    public decimal PortfolioCeiling { get; set; } = 1_000_000m;

    public decimal ForecastFor(decimal budget) => Math.Round(budget * ForecastMultiplier, 2);

    public void EnsureWithinCeiling(decimal forecast)
    {
        Guard.Against(
            forecast > PortfolioCeiling,
            "benefit.exceeds_portfolio_ceiling",
            $"Forecast benefit of {forecast:N0} exceeds the portfolio ceiling of {PortfolioCeiling:N0}; a separate business case is required.");
    }
}
