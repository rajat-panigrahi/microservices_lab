using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StrategyOps.BuildingBlocks.Api;
using StrategyOps.BuildingBlocks.Auth;
using StrategyOps.BuildingBlocks.Results;
using StrategyOps.BuildingBlocks.Time;
using StrategyOps.Identity.Api.Features.IssueToken;
using StrategyOps.Identity.Api.Infrastructure;
using TokenEntity = StrategyOps.Identity.Api.Domain.RefreshToken;

namespace StrategyOps.Identity.Api.Features.Refresh;

public sealed record RefreshRequest(string RefreshToken);

public sealed class RefreshRequestValidator : AbstractValidator<RefreshRequest>
{
    public RefreshRequestValidator() => RuleFor(x => x.RefreshToken).NotEmpty();
}

/// <summary>
/// Swaps a refresh token for a new access token - and a new refresh token.
/// </summary>
/// <remarks>
/// The old refresh token is revoked on use (<b>refresh token rotation</b>). Without rotation a
/// stolen refresh token works until it expires, silently, alongside the real user's. With it,
/// the thief and the victim end up racing: whoever refreshes second presents a revoked token,
/// which is a detectable signal that something is wrong.
/// </remarks>
public sealed class RefreshAccessTokenHandler(
    IdentityDbContext db,
    IOptions<JwtOptions> jwtOptions,
    IClock clock,
    ILogger<RefreshAccessTokenHandler> logger)
{
    public async Task<Result<TokenResponse>> HandleAsync(RefreshRequest request, CancellationToken ct)
    {
        var hash = TokenEntity.HashOf(request.RefreshToken);
        var now = clock.UtcNow;

        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored is null || !stored.IsUsable(now))
        {
            logger.LogWarning("Rejected a refresh token that was unknown, expired or already used");
            return Result<TokenResponse>.Invalid("auth.invalid_refresh_token", "That refresh token is not usable.");
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == stored.UserId, ct);

        if (user is null || user.IsDisabled)
        {
            // The access token could not be revoked, but this is where a disabled account is
            // finally locked out - within one access-token lifetime.
            stored.Revoke(now);
            await db.SaveChangesAsync(ct);
            return Result<TokenResponse>.Invalid("auth.invalid_refresh_token", "That refresh token is not usable.");
        }

        stored.Revoke(now);
        var (replacement, plainText) = TokenEntity.Issue(user.Id, now, jwtOptions.Value.RefreshTokenDays);
        db.RefreshTokens.Add(replacement);
        await db.SaveChangesAsync(ct);

        var accessToken = IssueTokenHandler.CreateAccessToken(user, jwtOptions.Value, now);

        return Result<TokenResponse>.Ok(new TokenResponse(
            accessToken,
            "Bearer",
            jwtOptions.Value.AccessTokenMinutes * 60,
            plainText,
            user.DisplayName,
            user.Role));
    }
}

public sealed class RefreshAccessTokenEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/connect/refresh", async (
                RefreshRequest request,
                RefreshAccessTokenHandler handler,
                CancellationToken ct) => (await handler.HandleAsync(request, ct)).ToHttpResult())
            .WithName("RefreshAccessToken")
            .WithSummary("Exchange a refresh token for a new access token, rotating the refresh token")
            .WithTags("Identity")
            .WithValidation<RefreshRequest>()
            .AllowAnonymous()
            .Produces<TokenResponse>();
}
