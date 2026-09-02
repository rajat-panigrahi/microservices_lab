using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Auth;
using StrategyOps.Identity.Api.Domain;

namespace StrategyOps.Identity.Api.Infrastructure;

/// <summary>
/// Creates the four demo accounts on first run.
/// </summary>
/// <remarks>
/// Seeded passwords come from configuration, not from a literal in this file, so the same
/// code can run in an environment where they are real secrets. The default is a well-known
/// value and the service logs a warning about it - a seeded default that is silently insecure
/// is worse than one that shouts.
/// </remarks>
public static class IdentitySeeder
{
    public const string DefaultPassword = "Passw0rd!";

    public static async Task SeedAsync(IdentityDbContext db, string password, ILogger logger, CancellationToken ct = default)
    {
        if (await db.Users.AnyAsync(ct))
        {
            return;
        }

        db.Users.AddRange(
            PortfolioUser.Create("portfolio.director", "Priya Director", Roles.PortfolioDirector, password),
            PortfolioUser.Create("project.manager", "Marcus Manager", Roles.ProjectManager, password),
            PortfolioUser.Create("risk.owner", "Rosa Owner", Roles.RiskOwner, password),
            PortfolioUser.Create("viewer", "Vikram Viewer", Roles.Viewer, password));

        await db.SaveChangesAsync(ct);

        if (password == DefaultPassword)
        {
            logger.LogWarning(
                "Seeded four demo accounts with the well-known default password. Set Identity:SeedPassword before exposing this anywhere.");
        }
    }
}
