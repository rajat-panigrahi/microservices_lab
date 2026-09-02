namespace StrategyOps.Reporting.Api.Features.RebuildReadModel;

/// <summary>
/// Where the source services live, for the rebuild path only.
/// </summary>
/// <remarks>
/// Note that these are used by <b>one</b> endpoint. Normal operation of this service uses no
/// HTTP at all - it lives entirely off events. That asymmetry is the point: async for the
/// steady state, sync only when you deliberately need a consistent snapshot right now.
/// </remarks>
public sealed class UpstreamServices
{
    public const string SectionName = "Upstream";

    public string Projects { get; set; } = "http://localhost:5101";

    public string Kpi { get; set; } = "http://localhost:5102";

    public string Risk { get; set; } = "http://localhost:5103";

    public string Issues { get; set; } = "http://localhost:5104";

    public string Benefits { get; set; } = "http://localhost:5105";
}

// Response shapes of the upstream endpoints. Deliberately local records rather than shared
// types: sharing DTOs between a producer and its consumer is how two services quietly become
// one deployable.
public sealed record UpstreamProject(Guid Id, string Code, string Name, string Stage, string Health, decimal Budget);

public sealed record UpstreamProjectPage(List<UpstreamProject> Items, int Page, int PageSize, int TotalCount);

public sealed record UpstreamKpi(string Rag);

public sealed record UpstreamScorecard(int GreenCount, int AmberCount, int RedCount, int NotMeasuredCount, List<UpstreamKpi> Kpis);

public sealed record UpstreamRisk(string Tier, string Status);

public sealed record UpstreamRiskRegister(int OpenCount, int CriticalOpenCount, List<UpstreamRisk> Risks);

public sealed record UpstreamIssue(string Severity, string Status);

public sealed record UpstreamBenefit(decimal ForecastValue, decimal RealisedToDate, decimal RealisationPercent, string Status);
