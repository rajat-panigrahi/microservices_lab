using StrategyOps.BuildingBlocks.Domain;

namespace StrategyOps.Risk.Api.Domain;

/// <summary>
/// A project's risk register. Created by the initiation saga, not by a user - which is what
/// makes "provision the register" a compensatable step: if another leg of the saga fails,
/// this register is deleted again.
/// </summary>
public sealed class RiskRegister
{
    private RiskRegister()
    {
    }

    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    public string ProjectCode { get; private set; } = string.Empty;

    public RiskRegisterStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static RiskRegister Provision(Guid projectId, string projectCode, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        ProjectId = Guard.AgainstEmpty(projectId, "register.project_required", "A risk register belongs to a project."),
        ProjectCode = Guard.AgainstBlank(projectCode, "register.project_code_required", "A risk register needs the project code."),
        Status = RiskRegisterStatus.Active,
        CreatedAtUtc = now
    };

    public void Close()
    {
        Status = RiskRegisterStatus.Closed;
    }

    public void EnsureAcceptingRisks()
    {
        Guard.Against(
            Status == RiskRegisterStatus.Closed,
            "register.closed",
            "This project's risk register is closed; no further risks can be raised.");
    }
}
