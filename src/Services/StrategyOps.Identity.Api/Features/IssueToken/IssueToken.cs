using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using StrategyOps.BuildingBlocks.Api;
using StrategyOps.BuildingBlocks.Auth;
using StrategyOps.BuildingBlocks.Results;
using StrategyOps.BuildingBlocks.Time;
using StrategyOps.Identity.Api.Domain;
using StrategyOps.Identity.Api.Infrastructure;

// The aggregate is called RefreshToken and there is a slice by a similar name, so the bare
// name is ambiguous here. An alias keeps both readable.
using RefreshTokenEntity = StrategyOps.Identity.Api.Domain.RefreshToken;

namespace StrategyOps.Identity.Api.Features.IssueToken;

public sealed record TokenRequest(string UserName, string Password);

public sealed record TokenResponse(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    string RefreshToken,
    string DisplayName,
    string Role);

public sealed class TokenRequestValidator : AbstractValidator<TokenRequest>
{
    public TokenRequestValidator()
    {
        RuleFor(x => x.UserName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Password).NotEmpty();
    }
}

/// <summary>
/// Exchanges a username and password for a signed JWT plus a refresh token.
/// </summary>
/// <remarks>
/// <para>
/// This is OAuth 2.0's <b>resource owner password credentials</b> grant in spirit, and it is
/// used here only because it is the shortest thing to demonstrate with curl. It is
/// <b>discouraged in OAuth 2.1</b> for a good reason: the client application handles the
/// user's actual password, which rules out MFA, federation and consent screens.
/// </para>
/// <para>
/// A real system uses <b>authorization code with PKCE</b>: the browser goes to the identity
/// provider, the user authenticates there (and only there), and the application receives a
/// code it exchanges for tokens - never seeing the password at all. Machine-to-machine calls
/// use the <b>client credentials</b> grant.
/// </para>
/// <para>
/// Knowing which grant to use, and why this one is the wrong one for a real login, is the
/// substance behind "explain OAuth" - not being able to recite what the letters stand for.
/// </para>
/// </remarks>
public sealed class IssueTokenHandler(
    IdentityDbContext db,
    IOptions<JwtOptions> jwtOptions,
    IClock clock,
    ILogger<IssueTokenHandler> logger)
{
    public async Task<Result<TokenResponse>> HandleAsync(TokenRequest request, CancellationToken ct)
    {
        var userName = request.UserName.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.UserName == userName, ct);

        // One message for "no such user" and "wrong password". Distinguishing them tells an
        // attacker which usernames exist, which is the first half of the work.
        if (user is null || !user.PasswordMatches(request.Password))
        {
            logger.LogWarning("Rejected sign-in for {UserName}", userName);
            return Result<TokenResponse>.Invalid("auth.invalid_credentials", "Username or password is incorrect.");
        }

        var options = jwtOptions.Value;
        var now = clock.UtcNow;

        var (refreshToken, refreshPlainText) = RefreshTokenEntity.Issue(user.Id, now, options.RefreshTokenDays);
        db.RefreshTokens.Add(refreshToken);
        await db.SaveChangesAsync(ct);

        var accessToken = CreateAccessToken(user, options, now);

        return Result<TokenResponse>.Ok(new TokenResponse(
            accessToken,
            "Bearer",
            options.AccessTokenMinutes * 60,
            refreshPlainText,
            user.DisplayName,
            user.Role));
    }

    internal static string CreateAccessToken(PortfolioUser user, JwtOptions options, DateTimeOffset now)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        // Only what every service needs to make an authorisation decision. A token is not a
        // place to cache the user's profile: it is sent on every request, it cannot be
        // revoked, and anything in it is readable by anyone who intercepts it - a JWT is
        // signed, not encrypted.
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.UserName),
            new(ClaimTypes.NameIdentifier, user.UserName),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Role, user.Role),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("n"))
        };

        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: now.AddMinutes(options.AccessTokenMinutes).UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public sealed class IssueTokenEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/connect/token", async (
                TokenRequest request,
                IssueTokenHandler handler,
                CancellationToken ct) => (await handler.HandleAsync(request, ct)).ToHttpResult())
            .WithName("IssueToken")
            .WithSummary("Exchange credentials for an access token and a refresh token")
            .WithTags("Identity")
            .WithValidation<TokenRequest>()
            .AllowAnonymous()
            .Produces<TokenResponse>();
}
