using Shouldly;
using StrategyOps.BuildingBlocks.Domain;
using StrategyOps.Issues.Api.Domain;

namespace StrategyOps.Domain.Tests.Issues;

public class IssueTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid ProjectId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static Issue AnIssue(IssueSeverity severity = IssueSeverity.Medium) =>
        Issue.Raise(ProjectId, "Integration deadline missed", severity, Now);

    [Theory]
    [InlineData(IssueSeverity.Critical, 2)]
    [InlineData(IssueSeverity.High, 5)]
    [InlineData(IssueSeverity.Medium, 10)]
    [InlineData(IssueSeverity.Low, 20)]
    public void Raise_SetsAResolutionTargetFromTheSeveritySla(IssueSeverity severity, int expectedDays)
    {
        var issue = AnIssue(severity);

        issue.TargetResolutionUtc.ShouldBe(Now.AddDays(expectedDays));
    }

    [Fact]
    public void Raise_StartsAsNewWithNoOwner()
    {
        var issue = AnIssue();

        issue.Status.ShouldBe(IssueStatus.New);
        issue.Owner.ShouldBeNull();
        issue.OriginRiskId.ShouldBeNull();
    }

    [Theory]
    [InlineData("Critical", IssueSeverity.Critical)]
    [InlineData("High", IssueSeverity.High)]
    [InlineData("Medium", IssueSeverity.Medium)]
    [InlineData("Low", IssueSeverity.Medium)]
    [InlineData("something-unrecognised", IssueSeverity.Medium)]
    public void RaiseFromRisk_MapsTheRiskTierOntoIssueSeverity(string riskTier, IssueSeverity expected)
    {
        var issue = Issue.RaiseFromRisk(ProjectId, Guid.NewGuid(), "Supplier missed the date", riskTier, Now);

        issue.Severity.ShouldBe(expected);
    }

    [Fact]
    public void RaiseFromRisk_KeepsTheLinkBackToTheOriginatingRisk()
    {
        var riskId = Guid.NewGuid();

        var issue = Issue.RaiseFromRisk(ProjectId, riskId, "Supplier missed the date", "Critical", Now);

        issue.OriginRiskId.ShouldBe(riskId);
    }

    [Fact]
    public void Assign_MovesTheIssueToAssigned()
    {
        var issue = AnIssue();

        issue.Assign("I. Owner");

        issue.Status.ShouldBe(IssueStatus.Assigned);
        issue.Owner.ShouldBe("I. Owner");
    }

    [Fact]
    public void Start_RequiresAnOwner()
    {
        var issue = AnIssue();

        var act = () => issue.Start();

        act.ShouldThrow<DomainException>().Code.ShouldBe("issue.invalid_status_transition");
    }

    [Fact]
    public void Resolve_RecordsTheNotesAndTheTime()
    {
        var issue = AnIssue();
        issue.Assign("I. Owner");
        issue.Start();

        issue.Resolve("Supplier re-planned; new date agreed", Now.AddDays(1));

        issue.Status.ShouldBe(IssueStatus.Resolved);
        issue.ResolvedAtUtc.ShouldBe(Now.AddDays(1));
        issue.ResolutionNotes.ShouldNotBeNull();
    }

    [Fact]
    public void Resolve_IsAllowedDirectlyFromAssignedWithoutStarting()
    {
        var issue = AnIssue();
        issue.Assign("I. Owner");

        issue.Resolve("Turned out to be a false alarm", Now);

        issue.Status.ShouldBe(IssueStatus.Resolved);
    }

    [Fact]
    public void Resolve_IsRejectedForAnUnassignedIssue()
    {
        var issue = AnIssue();

        var act = () => issue.Resolve("cannot resolve what nobody owns", Now);

        act.ShouldThrow<DomainException>().Code.ShouldBe("issue.invalid_status_transition");
    }

    [Fact]
    public void BreachedSla_IsTrueOnlyOnceTheTargetHasPassedAndTheIssueIsStillOpen()
    {
        var issue = AnIssue(IssueSeverity.Critical);

        issue.HasBreachedSla(Now.AddDays(1)).ShouldBeFalse();
        issue.HasBreachedSla(Now.AddDays(3)).ShouldBeTrue();

        issue.Assign("I. Owner");
        issue.Resolve("done", Now.AddDays(3));

        issue.HasBreachedSla(Now.AddDays(10)).ShouldBeFalse("a resolved issue is no longer breaching");
    }
}
