using Shouldly;
using StrategyOps.Benefits.Api.Domain;
using StrategyOps.BuildingBlocks.Domain;

namespace StrategyOps.Domain.Tests.Benefits;

public class BenefitProfileTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid ProjectId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private static BenefitProfile AProfile(decimal forecast = 350_000m) =>
        BenefitProfile.Register(ProjectId, "PRJ-0007", "Warehouse automation savings", BenefitType.Cashable, forecast, Now);

    [Fact]
    public void Register_StartsAtZeroRealised()
    {
        var profile = AProfile();

        profile.Status.ShouldBe(BenefitStatus.Registered);
        profile.RealisedToDate.ShouldBe(0m);
        profile.RealisationPercent.ShouldBe(0m);
    }

    [Fact]
    public void Register_RejectsANonPositiveForecast()
    {
        var act = () => AProfile(0m);

        act.ShouldThrow<DomainException>().Code.ShouldBe("benefit.forecast_must_be_positive");
    }

    [Fact]
    public void Realise_AccumulatesAndTracksThePercentage()
    {
        var profile = AProfile(400_000m);

        profile.Realise(100_000m);
        profile.RealisationPercent.ShouldBe(25m);

        profile.Realise(100_000m);
        profile.RealisedToDate.ShouldBe(200_000m);
        profile.RealisationPercent.ShouldBe(50m);
        profile.Status.ShouldBe(BenefitStatus.Realising);
    }

    [Fact]
    public void Realise_ReportsOverDeliveryRatherThanCappingAt100()
    {
        var profile = AProfile(100_000m);

        profile.Realise(150_000m);

        profile.RealisationPercent.ShouldBe(150m);
    }

    [Fact]
    public void FlagAtRisk_ReportsOnlyTheFirstSignal()
    {
        var profile = AProfile();

        profile.FlagAtRisk("Critical issue raised").ShouldBeTrue();
        profile.FlagAtRisk("KPI breached as well").ShouldBeFalse("already at risk; do not publish twice");

        profile.Status.ShouldBe(BenefitStatus.AtRisk);
        profile.AtRiskReason.ShouldBe("Critical issue raised");
    }

    [Fact]
    public void Realise_ClearsTheAtRiskFlagBecauseValueActuallyLanded()
    {
        var profile = AProfile();
        profile.FlagAtRisk("Critical issue raised");

        profile.Realise(50_000m);

        profile.Status.ShouldBe(BenefitStatus.Realising);
        profile.AtRiskReason.ShouldBeNull();
    }

    [Fact]
    public void AClosedProfile_RejectsFurtherRealisationAndCannotBeFlagged()
    {
        var profile = AProfile();
        profile.Close();

        var act = () => profile.Realise(1m);
        act.ShouldThrow<DomainException>().Code.ShouldBe("benefit.closed");

        profile.FlagAtRisk("too late").ShouldBeFalse();
    }
}

public class PortfolioBenefitPolicyTests
{
    [Fact]
    public void ForecastIsDerivedFromBudget()
    {
        var policy = new PortfolioBenefitPolicy { ForecastMultiplier = 1.4m };

        policy.ForecastFor(250_000m).ShouldBe(350_000m);
    }

    [Fact]
    public void AForecastWithinTheCeilingIsAccepted()
    {
        var policy = new PortfolioBenefitPolicy { PortfolioCeiling = 1_000_000m };

        Should.NotThrow(() => policy.EnsureWithinCeiling(350_000m));
    }

    [Fact]
    public void AForecastOverTheCeilingIsRejected_WhichIsWhatForcesTheSagaToCompensate()
    {
        var policy = new PortfolioBenefitPolicy { PortfolioCeiling = 1_000_000m };

        var act = () => policy.EnsureWithinCeiling(1_400_000m);

        act.ShouldThrow<DomainException>().Code.ShouldBe("benefit.exceeds_portfolio_ceiling");
    }
}
