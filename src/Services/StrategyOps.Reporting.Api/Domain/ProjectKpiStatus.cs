namespace StrategyOps.Reporting.Api.Domain;

/// <summary>
/// The current RAG of one KPI, kept so the read model can move a KPI between buckets.
/// </summary>
/// <remarks>
/// <para>
/// This exists because <c>KpiMeasurementRecorded</c> carries the new status but not the old
/// one, and the projection has to know which bucket to decrement. The first version of this
/// projection tried to infer it from the counts alone and got it wrong the moment a KPI
/// recovered - a test caught it.
/// </para>
/// <para>
/// The general lesson: a projection often needs a little state of its own beyond the row it
/// serves. That is fine - it is still all derived, and it all gets thrown away and rebuilt
/// together. The alternative, widening the event to carry the previous status, couples every
/// consumer to a detail only this one needs.
/// </para>
/// </remarks>
public sealed class ProjectKpiStatus
{
    public Guid ProjectId { get; set; }

    public Guid KpiId { get; set; }

    public string Rag { get; set; } = "NotMeasured";
}
