namespace StrategyOps.Reporting.Api.Domain;

/// <summary>
/// One flat row per project, denormalised across five services.
/// </summary>
/// <remarks>
/// <para>
/// This is the <b>read side</b> of CQRS, and it exists because the question the portfolio
/// director asks - "show me every project with its RAG, its open risks, its open issues and
/// its benefit realisation" - cannot be answered by any one service. Each service owns its
/// own database, so there is no join to write. The alternatives are:
/// </para>
/// <list type="number">
///   <item>have the gateway call five services per project on every page load - N+1 across
///   the network, and the page is down whenever any one service is;</item>
///   <item>let the dashboard query five databases directly - which abandons
///   database-per-service and couples the dashboard to everyone's schema;</item>
///   <item>keep a copy, updated by the events those services already publish. That is this.</item>
/// </list>
/// <para>
/// The cost is that this row is <b>eventually</b> consistent - it lags the source services by
/// however long the outbox and the broker take. The dashboard shows that lag on purpose.
/// </para>
/// <para>
/// Nothing here is a source of truth. Every field is a copy, and the row can be deleted and
/// rebuilt at any time - which is exactly what <c>POST /reporting/rebuild</c> does.
/// </para>
/// </remarks>
public sealed class PortfolioScorecard
{
    public Guid ProjectId { get; set; }

    public string ProjectCode { get; set; } = string.Empty;

    public string ProjectName { get; set; } = string.Empty;

    public string Stage { get; set; } = "Draft";

    public string Health { get; set; } = "Green";

    public string? HealthReason { get; set; }

    public decimal Budget { get; set; }

    // --- KPI, copied from the KPI service -----------------------------------

    /// <summary>How many KPIs the scorecard has in total, measured or not.</summary>
    public int KpiTotal { get; set; }

    public int KpiGreen { get; set; }

    public int KpiAmber { get; set; }

    public int KpiRed { get; set; }

    public int KpiNotMeasured { get; set; }

    // --- Risk, copied from the Risk service ---------------------------------
    public int OpenRisks { get; set; }

    public int CriticalOpenRisks { get; set; }

    public int EscalatedRisks { get; set; }

    // --- Issues, copied from the Issues service -----------------------------
    public int OpenIssues { get; set; }

    public int CriticalOpenIssues { get; set; }

    // --- Benefits, copied from the Benefits service -------------------------
    public decimal BenefitForecast { get; set; }

    public decimal BenefitRealised { get; set; }

    public decimal RealisationPercent { get; set; }

    public string BenefitStatus { get; set; } = "None";

    /// <summary>When this copy last changed - i.e. how stale the row you are reading is.</summary>
    public DateTimeOffset LastUpdatedUtc { get; set; }

    /// <summary>
    /// A single at-a-glance verdict, computed from the copies rather than stored anywhere.
    /// </summary>
    public string OverallStatus =>
        Stage switch
        {
            "Closed" => "Closed",
            "InitiationFailed" => "Failed",
            _ when Health == "Red" || CriticalOpenIssues > 0 || KpiRed > 0 => "Red",
            _ when Health == "Amber" || OpenIssues > 0 || KpiAmber > 0 || CriticalOpenRisks > 0 => "Amber",
            _ => "Green"
        };
}
