namespace StrategyOps.BuildingBlocks.Auth;

/// <summary>
/// The four roles in the portfolio office. Deliberately modelled on who actually does what,
/// not on CRUD - "CanWriteProjects" tells you nothing about whether a risk owner should be
/// able to close someone else's project.
/// </summary>
public static class Roles
{
    /// <summary>Owns the portfolio: can initiate and close projects, and see everything.</summary>
    public const string PortfolioDirector = "PortfolioDirector";

    /// <summary>Runs individual projects: raises risks and issues, records measurements.</summary>
    public const string ProjectManager = "ProjectManager";

    /// <summary>Owns and escalates risks, and resolves the issues they become.</summary>
    public const string RiskOwner = "RiskOwner";

    /// <summary>Read-only. Auditors, finance, anyone who should never change anything.</summary>
    public const string Viewer = "Viewer";

    public static readonly string[] All = [PortfolioDirector, ProjectManager, RiskOwner, Viewer];
}

/// <summary>
/// Named policies, so endpoints say what they need rather than listing roles inline.
/// </summary>
/// <remarks>
/// The indirection earns itself the first time a role is added: one policy definition changes
/// instead of thirty endpoint attributes scattered across six services.
/// </remarks>
public static class Policies
{
    /// <summary>Initiating and closing projects - a portfolio-level decision.</summary>
    public const string ManagePortfolio = "portfolio:manage";

    /// <summary>Day-to-day delivery: creating projects, recording measurements.</summary>
    public const string ManageDelivery = "delivery:manage";

    /// <summary>Raising, scoring and escalating risks and issues.</summary>
    public const string ManageRisk = "risk:manage";

    /// <summary>Any authenticated user, including Viewer.</summary>
    public const string Read = "read";
}
