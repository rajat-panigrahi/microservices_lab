using Shouldly;
using StrategyOps.BuildingBlocks.Domain;
using StrategyOps.Projects.Api.Domain;

namespace StrategyOps.Domain.Tests.Projects;

/// <summary>
/// The Project aggregate is the only place that knows which stage transitions are legal.
/// These tests were written before the aggregate existed - they are the specification.
/// </summary>
public class ProjectTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid ObjectiveId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static Project ADraft() =>
        Project.CreateDraft("PRJ-0007", "Warehouse automation", ObjectiveId, "A. Sponsor", 250_000m, Now);

    private static Project AnActiveProject()
    {
        var project = ADraft();
        project.SubmitForInitiation(Now);
        project.CompleteInitiation(Now);
        return project;
    }

    [Fact]
    public void CreateDraft_StartsInDraftStageAndGreenHealth()
    {
        var project = ADraft();

        project.Stage.ShouldBe(ProjectStage.Draft);
        project.Health.ShouldBe(ProjectHealth.Green);
        project.Code.ShouldBe("PRJ-0007");
        project.CreatedAtUtc.ShouldBe(Now);
    }

    [Fact]
    public void CreateDraft_UppercasesAndTrimsTheCode()
    {
        var project = Project.CreateDraft("  prj-0007 ", "Warehouse automation", ObjectiveId, "A. Sponsor", 1m, Now);

        project.Code.ShouldBe("PRJ-0007");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateDraft_RejectsABlankName(string name)
    {
        var act = () => Project.CreateDraft("PRJ-0007", name, ObjectiveId, "A. Sponsor", 1m, Now);

        act.ShouldThrow<DomainException>().Code.ShouldBe("project.name_required");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CreateDraft_RejectsANonPositiveBudget(decimal budget)
    {
        var act = () => Project.CreateDraft("PRJ-0007", "Warehouse automation", ObjectiveId, "A. Sponsor", budget, Now);

        act.ShouldThrow<DomainException>().Code.ShouldBe("project.budget_must_be_positive");
    }

    [Fact]
    public void CreateDraft_RequiresAnObjectiveToDeliverAgainst()
    {
        var act = () => Project.CreateDraft("PRJ-0007", "Warehouse automation", Guid.Empty, "A. Sponsor", 1m, Now);

        act.ShouldThrow<DomainException>().Code.ShouldBe("project.objective_required");
    }

    [Fact]
    public void SubmitForInitiation_MovesADraftToInitiating()
    {
        var project = ADraft();

        project.SubmitForInitiation(Now);

        project.Stage.ShouldBe(ProjectStage.Initiating);
    }

    [Fact]
    public void SubmitForInitiation_IsRejectedForAProjectThatIsAlreadyActive()
    {
        var project = AnActiveProject();

        var act = () => project.SubmitForInitiation(Now);

        act.ShouldThrow<DomainException>().Code.ShouldBe("project.invalid_stage_transition");
    }

    [Fact]
    public void CompleteInitiation_MovesInitiatingToActive()
    {
        var project = ADraft();
        project.SubmitForInitiation(Now);

        project.CompleteInitiation(Now);

        project.Stage.ShouldBe(ProjectStage.Active);
        project.ActivatedAtUtc.ShouldBe(Now);
    }

    [Fact]
    public void CompleteInitiation_IsRejectedFromDraft()
    {
        var project = ADraft();

        var act = () => project.CompleteInitiation(Now);

        act.ShouldThrow<DomainException>().Code.ShouldBe("project.invalid_stage_transition");
    }

    [Fact]
    public void FailInitiation_RecordsTheReasonAndParksTheProject()
    {
        var project = ADraft();
        project.SubmitForInitiation(Now);

        project.FailInitiation("benefit profile rejected: forecast exceeds portfolio ceiling");

        project.Stage.ShouldBe(ProjectStage.InitiationFailed);
        project.FailureReason.ShouldNotBeNull().ShouldContain("portfolio ceiling");
    }

    [Fact]
    public void FailInitiation_LeavesTheProjectResubmittable()
    {
        var project = ADraft();
        project.SubmitForInitiation(Now);
        project.FailInitiation("kpi service unavailable");

        project.SubmitForInitiation(Now);

        project.Stage.ShouldBe(ProjectStage.Initiating);
        project.FailureReason.ShouldBeNull();
    }

    [Fact]
    public void SetHealth_ReportsThatSomethingChangedOnlyWhenItActuallyDid()
    {
        var project = AnActiveProject();

        project.SetHealth(ProjectHealth.Amber, "critical risk escalated").ShouldBeTrue();
        project.SetHealth(ProjectHealth.Amber, "same risk reported twice").ShouldBeFalse();

        project.Health.ShouldBe(ProjectHealth.Amber);
        project.HealthReason.ShouldBe("critical risk escalated");
    }

    [Fact]
    public void SetHealth_IsRejectedOnceTheProjectIsClosed()
    {
        var project = AnActiveProject();
        project.Close(Now);

        var act = () => { project.SetHealth(ProjectHealth.Red, "too late"); };

        act.ShouldThrow<DomainException>().Code.ShouldBe("project.closed");
    }

    [Fact]
    public void PutOnHold_AndResume_RoundTripThroughActive()
    {
        var project = AnActiveProject();

        project.PutOnHold("funding paused");
        project.Stage.ShouldBe(ProjectStage.OnHold);

        project.Resume();
        project.Stage.ShouldBe(ProjectStage.Active);
    }

    [Fact]
    public void PutOnHold_IsRejectedForADraft()
    {
        var project = ADraft();

        var act = () => project.PutOnHold("funding paused");

        act.ShouldThrow<DomainException>().Code.ShouldBe("project.invalid_stage_transition");
    }

    [Theory]
    [InlineData(ProjectStage.Active)]
    [InlineData(ProjectStage.OnHold)]
    public void Close_IsAllowedFromActiveAndOnHold(ProjectStage from)
    {
        var project = AnActiveProject();
        if (from == ProjectStage.OnHold)
        {
            project.PutOnHold("funding paused");
        }

        project.Close(Now);

        project.Stage.ShouldBe(ProjectStage.Closed);
        project.ClosedAtUtc.ShouldBe(Now);
    }

    [Fact]
    public void Close_IsRejectedForADraftThatWasNeverInitiated()
    {
        var project = ADraft();

        var act = () => project.Close(Now);

        act.ShouldThrow<DomainException>().Code.ShouldBe("project.invalid_stage_transition");
    }

    [Fact]
    public void Close_IsIdempotentlyRejectedForAnAlreadyClosedProject()
    {
        var project = AnActiveProject();
        project.Close(Now);

        var act = () => project.Close(Now);

        act.ShouldThrow<DomainException>().Code.ShouldBe("project.invalid_stage_transition");
    }
}
