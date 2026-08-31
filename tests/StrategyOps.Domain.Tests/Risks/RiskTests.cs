using Shouldly;
using StrategyOps.BuildingBlocks.Domain;
using StrategyOps.Risk.Api.Domain;

// The aggregate is called Risk and it lives under the StrategyOps.Risk namespace, so the
// bare name binds to the namespace here. An alias is the tidiest way through that.
using RiskEntry = StrategyOps.Risk.Api.Domain.Risk;

namespace StrategyOps.Domain.Tests.Risks;

/// <summary>
/// Risk scoring is a 5x5 probability/impact matrix - the thing every PMO in the world
/// actually uses. The tier boundaries are the rules worth pinning down.
/// </summary>
public class RiskTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid RegisterId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static RiskEntry ARisk(int probability = 3, int impact = 3) =>
        RiskEntry.Raise(RegisterId, "Supplier cannot meet the integration deadline", "Supplier", probability, impact, "R. Owner", Now);

    [Theory]
    [InlineData(1, 1, 1, RiskTier.Low)]
    [InlineData(2, 2, 4, RiskTier.Low)]
    [InlineData(1, 5, 5, RiskTier.Medium)]
    [InlineData(3, 3, 9, RiskTier.Medium)]
    [InlineData(2, 5, 10, RiskTier.High)]
    [InlineData(3, 5, 15, RiskTier.High)]
    [InlineData(4, 4, 16, RiskTier.Critical)]
    [InlineData(5, 5, 25, RiskTier.Critical)]
    public void Scoring_MultipliesProbabilityByImpactAndBandsTheResult(int probability, int impact, int expectedScore, RiskTier expectedTier)
    {
        var risk = ARisk(probability, impact);

        risk.Score.ShouldBe(expectedScore);
        risk.Tier.ShouldBe(expectedTier);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void Raise_RejectsAProbabilityOutsideTheMatrix(int probability)
    {
        var act = () => ARisk(probability: probability);

        act.ShouldThrow<DomainException>().Code.ShouldBe("risk.probability_out_of_range");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Raise_RejectsAnImpactOutsideTheMatrix(int impact)
    {
        var act = () => ARisk(impact: impact);

        act.ShouldThrow<DomainException>().Code.ShouldBe("risk.impact_out_of_range");
    }

    [Fact]
    public void Raise_StartsOpenWithNoMitigation()
    {
        var risk = ARisk();

        risk.Status.ShouldBe(RiskStatus.Open);
        risk.MitigationPlan.ShouldBeNull();
        risk.RaisedAtUtc.ShouldBe(Now);
    }

    [Fact]
    public void Rescore_RecalculatesTheTier()
    {
        var risk = ARisk(2, 2);
        risk.Tier.ShouldBe(RiskTier.Low);

        risk.Rescore(5, 5);

        risk.Score.ShouldBe(25);
        risk.Tier.ShouldBe(RiskTier.Critical);
    }

    [Fact]
    public void PlanMitigation_MovesTheRiskToMitigating()
    {
        var risk = ARisk();

        risk.PlanMitigation("Dual-source the integration work and hold a two-week buffer");

        risk.Status.ShouldBe(RiskStatus.Mitigating);
        risk.MitigationPlan.ShouldNotBeNull();
    }

    [Fact]
    public void Escalate_MarksTheRiskMaterialisedAndRemembersWhy()
    {
        var risk = ARisk(5, 5);

        risk.Escalate("Supplier confirmed they will miss the date", Now);

        risk.Status.ShouldBe(RiskStatus.Materialised);
        risk.EscalatedAtUtc.ShouldBe(Now);
        risk.EscalationReason.ShouldNotBeNull().ShouldContain("miss the date");
    }

    [Fact]
    public void Escalate_IsAllowedFromMitigating()
    {
        var risk = ARisk(5, 5);
        risk.PlanMitigation("Dual-source the integration work");

        risk.Escalate("Mitigation did not land in time", Now);

        risk.Status.ShouldBe(RiskStatus.Materialised);
    }

    [Fact]
    public void Escalate_IsRejectedForAClosedRisk()
    {
        var risk = ARisk();
        risk.Close("Supplier delivered early");

        var act = () => risk.Escalate("too late", Now);

        act.ShouldThrow<DomainException>().Code.ShouldBe("risk.invalid_status_transition");
    }

    [Fact]
    public void Escalate_Twice_IsRejectedSoOnlyOneIssueIsEverRaised()
    {
        var risk = ARisk(5, 5);
        risk.Escalate("Supplier confirmed they will miss the date", Now);

        var act = () => risk.Escalate("reported again", Now);

        act.ShouldThrow<DomainException>().Code.ShouldBe("risk.invalid_status_transition");
    }

    [Fact]
    public void Rescore_IsRejectedForAClosedRisk()
    {
        var risk = ARisk();
        risk.Close("no longer relevant");

        var act = () => risk.Rescore(5, 5);

        act.ShouldThrow<DomainException>().Code.ShouldBe("risk.invalid_status_transition");
    }

    [Fact]
    public void Close_RecordsTheResolution()
    {
        var risk = ARisk();

        risk.Close("Supplier delivered early");

        risk.Status.ShouldBe(RiskStatus.Closed);
        risk.Resolution.ShouldBe("Supplier delivered early");
    }
}
