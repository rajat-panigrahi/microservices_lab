using Shouldly;
using StrategyOps.BuildingBlocks.Domain;
using StrategyOps.Kpi.Api.Domain;

namespace StrategyOps.Domain.Tests.Kpis;

/// <summary>
/// RAG banding is the whole point of a KPI, and it is direction-sensitive: 4.20 is a good
/// cost-per-order and a terrible customer-satisfaction score.
/// </summary>
public class KpiDefinitionTests
{
    private static readonly Guid ScorecardId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly DateTimeOffset Period = new(2026, 3, 31, 0, 0, 0, TimeSpan.Zero);

    private static KpiDefinition HigherIsBetter() =>
        KpiDefinition.Create(ScorecardId, "On-time delivery", "%", KpiDirection.HigherIsBetter, target: 95m, amberThreshold: 90m);

    private static KpiDefinition LowerIsBetter() =>
        KpiDefinition.Create(ScorecardId, "Cost per order", "GBP", KpiDirection.LowerIsBetter, target: 4m, amberThreshold: 5m);

    [Theory]
    [InlineData(99, KpiRag.Green)]
    [InlineData(95, KpiRag.Green)]
    [InlineData(94.9, KpiRag.Amber)]
    [InlineData(90, KpiRag.Amber)]
    [InlineData(89.9, KpiRag.Red)]
    [InlineData(0, KpiRag.Red)]
    public void HigherIsBetter_BandsOnTargetThenAmberThreshold(decimal value, KpiRag expected)
    {
        HigherIsBetter().Evaluate(value).ShouldBe(expected);
    }

    [Theory]
    [InlineData(3, KpiRag.Green)]
    [InlineData(4, KpiRag.Green)]
    [InlineData(4.5, KpiRag.Amber)]
    [InlineData(5, KpiRag.Amber)]
    [InlineData(5.1, KpiRag.Red)]
    public void LowerIsBetter_InvertsTheComparison(decimal value, KpiRag expected)
    {
        LowerIsBetter().Evaluate(value).ShouldBe(expected);
    }

    [Fact]
    public void ANewKpi_IsNotMeasuredRatherThanRed()
    {
        HigherIsBetter().Rag.ShouldBe(KpiRag.NotMeasured, "no data is not the same as bad data");
    }

    [Fact]
    public void Create_RejectsAnAmberThresholdOnTheWrongSideOfTheTarget()
    {
        var act = () => KpiDefinition.Create(ScorecardId, "On-time delivery", "%", KpiDirection.HigherIsBetter, target: 95m, amberThreshold: 99m);

        act.ShouldThrow<DomainException>().Code.ShouldBe("kpi.amber_threshold_invalid");
    }

    [Fact]
    public void Create_RejectsALowerIsBetterAmberThresholdBelowTheTarget()
    {
        var act = () => KpiDefinition.Create(ScorecardId, "Cost per order", "GBP", KpiDirection.LowerIsBetter, target: 4m, amberThreshold: 3m);

        act.ShouldThrow<DomainException>().Code.ShouldBe("kpi.amber_threshold_invalid");
    }

    [Fact]
    public void Record_ReturnsThePreviousRagSoTheCallerCanTellABreachFromASteadyState()
    {
        var kpi = HigherIsBetter();

        kpi.Record(99m, Period).ShouldBe(KpiRag.NotMeasured);
        kpi.Rag.ShouldBe(KpiRag.Green);

        kpi.Record(85m, Period).ShouldBe(KpiRag.Green, "was green, now red: this is a breach");
        kpi.Rag.ShouldBe(KpiRag.Red);

        kpi.Record(80m, Period).ShouldBe(KpiRag.Red, "was already red: not a new breach");
        kpi.Rag.ShouldBe(KpiRag.Red);

        kpi.Record(96m, Period).ShouldBe(KpiRag.Red, "was red, now green: this is a recovery");
        kpi.Rag.ShouldBe(KpiRag.Green);
    }

    [Fact]
    public void Record_KeepsTheLatestValueAndPeriod()
    {
        var kpi = HigherIsBetter();

        kpi.Record(92m, Period);

        kpi.LatestValue.ShouldBe(92m);
        kpi.LatestPeriodEndUtc.ShouldBe(Period);
    }

    [Fact]
    public void BaselineKpis_GiveEveryProjectAStartingScorecard()
    {
        var baseline = KpiScorecard.BaselineKpisFor(ScorecardId).ToList();

        baseline.Count.ShouldBe(3);
        baseline.ShouldAllBe(k => k.ScorecardId == ScorecardId);
        baseline.Select(k => k.Name).ShouldContain("Benefit realisation");
    }

    [Fact]
    public void AClosedScorecard_RejectsFurtherMeasurements()
    {
        var scorecard = KpiScorecard.Provision(Guid.NewGuid(), "PRJ-0007", Guid.NewGuid(), Period);
        scorecard.Close();

        var act = () => scorecard.EnsureAcceptingMeasurements();

        act.ShouldThrow<DomainException>().Code.ShouldBe("scorecard.closed");
    }
}
