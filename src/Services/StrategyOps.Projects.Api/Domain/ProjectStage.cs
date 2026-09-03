namespace StrategyOps.Projects.Api.Domain;

/// <summary>
/// Lifecycle of a project. <see cref="Initiating"/> is the interesting one: it is the window
/// during which the initiation saga is waiting on KPI, Risk and Benefits, and the project is
/// neither a draft nor live.
/// </summary>
public enum ProjectStage
{
    Draft,
    Initiating,
    Active,
    OnHold,
    Closed,
    InitiationFailed
}

/// <summary>RAG status. Driven by escalated risks and breached KPIs, not set by hand.</summary>
public enum ProjectHealth
{
    Green,
    Amber,
    Red
}
