using Microsoft.EntityFrameworkCore;
using StrategyOps.Risk.Api.Domain;

namespace StrategyOps.Risk.Api.Infrastructure;

/// <summary>
/// Shared query used by several slices: find a project's register and confirm it is open.
/// Small enough to be a static helper rather than a service - the slices stay independent,
/// they just do not each rewrite the same LINQ.
/// </summary>
public static class RiskRegisterLookup
{
    public static Task<RiskRegister?> ForProjectAsync(this RiskDbContext db, Guid projectId, CancellationToken ct) =>
        db.Registers.FirstOrDefaultAsync(r => r.ProjectId == projectId, ct);
}
