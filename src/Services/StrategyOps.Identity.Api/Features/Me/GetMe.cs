using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using StrategyOps.BuildingBlocks.Api;
using StrategyOps.BuildingBlocks.Auth;

namespace StrategyOps.Identity.Api.Features.Me;

public sealed record MeResponse(string? UserName, string? DisplayName, IReadOnlyCollection<string> Roles);

/// <summary>
/// Echoes back what the token says. Useful for a UI, and the quickest way to confirm a token
/// is being read the way you expect when authorisation is misbehaving.
/// </summary>
public sealed class GetMeEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/connect/me", (ICurrentUser user) =>
                Results.Ok(new MeResponse(user.UserName, user.DisplayName, user.Roles)))
            .WithName("GetMe")
            .WithSummary("Who does the presented token say you are?")
            .WithTags("Identity")
            .RequireAuthorization(Policies.Read)
            .Produces<MeResponse>();
}
