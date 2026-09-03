using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace StrategyOps.BuildingBlocks.Auth;

/// <summary>Who is making this request, for audit fields and ownership checks.</summary>
public interface ICurrentUser
{
    string? UserName { get; }

    string? DisplayName { get; }

    IReadOnlyCollection<string> Roles { get; }

    bool IsInRole(string role);
}

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string? UserName => accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);

    public string? DisplayName => accessor.HttpContext?.User.FindFirstValue(ClaimTypes.Name);

    public IReadOnlyCollection<string> Roles =>
        accessor.HttpContext?.User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray() ?? [];

    public bool IsInRole(string role) => accessor.HttpContext?.User.IsInRole(role) ?? false;
}
