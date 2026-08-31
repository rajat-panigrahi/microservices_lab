namespace StrategyOps.BuildingBlocks.Time;

/// <summary>
/// Time as a dependency, so "did this expire?" is testable without sleeping.
/// The saga timeout tests in phase 3 depend on this.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
