using Shouldly;
using StrategyOps.BuildingBlocks.Domain;
using StrategyOps.Projects.Api.Domain;

namespace StrategyOps.Domain.Tests.Projects;

public class StrategicObjectiveTests
{
    [Fact]
    public void Create_NormalisesTheCode()
    {
        var objective = StrategicObjective.Create(" so-01 ", "Reduce operating cost by 15%", "FY27", "COO");

        objective.Code.ShouldBe("SO-01");
        objective.Title.ShouldBe("Reduce operating cost by 15%");
        objective.Id.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void Create_RejectsABlankTitle()
    {
        var act = () => StrategicObjective.Create("SO-01", "  ", "FY27", "COO");

        act.ShouldThrow<DomainException>().Code.ShouldBe("objective.title_required");
    }

    [Fact]
    public void Create_RejectsABlankHorizon()
    {
        var act = () => StrategicObjective.Create("SO-01", "Reduce operating cost by 15%", "", "COO");

        act.ShouldThrow<DomainException>().Code.ShouldBe("objective.horizon_required");
    }
}
