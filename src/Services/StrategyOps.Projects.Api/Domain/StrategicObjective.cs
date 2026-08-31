using StrategyOps.BuildingBlocks.Domain;

namespace StrategyOps.Projects.Api.Domain;

/// <summary>
/// What the portfolio is trying to achieve, e.g. "Reduce operating cost by 15% by FY27".
/// Projects deliver against one; KPIs measure it; benefits prove it happened.
/// </summary>
public sealed class StrategicObjective
{
    private StrategicObjective()
    {
    }

    public Guid Id { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public string Horizon { get; private set; } = string.Empty;

    public string Owner { get; private set; } = string.Empty;

    public static StrategicObjective Create(string code, string title, string horizon, string owner) => new()
    {
        Id = Guid.NewGuid(),
        Code = Guard.AgainstBlank(code, "objective.code_required", "A strategic objective needs a code.").ToUpperInvariant(),
        Title = Guard.AgainstBlank(title, "objective.title_required", "A strategic objective needs a title."),
        Horizon = Guard.AgainstBlank(horizon, "objective.horizon_required", "A strategic objective needs a delivery horizon, e.g. FY27."),
        Owner = Guard.AgainstBlank(owner, "objective.owner_required", "A strategic objective needs an accountable owner.")
    };
}
