using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using StrategyOps.BuildingBlocks.Auth;

namespace StrategyOps.Slice.Tests;

/// <summary>
/// Mints real, correctly signed JWTs for the tests.
/// </summary>
/// <remarks>
/// <para>
/// The tempting alternative is to switch authentication off in the test environment, or to
/// install a fake "always authenticated" scheme. Both are worse, and for the same reason:
/// they make the tests pass on a configuration that is never deployed. A route that forgot
/// its policy, a role name typo, a fallback policy that is not actually applied - none of
/// those would be caught.
/// </para>
/// <para>
/// Signing a genuine token with the test signing key means the production authentication
/// pipeline runs unchanged, and the tests can also assert the negative cases: that a Viewer
/// gets 403 and an anonymous caller gets 401.
/// </para>
/// </remarks>
public static class TestTokens
{
    public const string SigningKey = "integration-test-signing-key-at-least-32-chars-long";
    public const string Issuer = "https://strategyops.local/identity";
    public const string Audience = "strategyops-api";

    public static string For(string role, string userName = "test.user")
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userName),
                new Claim(ClaimTypes.NameIdentifier, userName),
                new Claim(ClaimTypes.Name, $"Test {role}"),
                new Claim(ClaimTypes.Role, role)
            ],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>An expired token, for asserting that lifetime validation is actually on.</summary>
    public static string Expired(string role = Roles.PortfolioDirector)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: [new Claim(ClaimTypes.Role, role)],
            notBefore: DateTime.UtcNow.AddHours(-2),
            expires: DateTime.UtcNow.AddHours(-1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>A well-formed token signed with the wrong key - the forgery case.</summary>
    public static string WrongKey(string role = Roles.PortfolioDirector)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes("a-completely-different-key-that-is-also-32-chars")),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: [new Claim(ClaimTypes.Role, role)],
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static HttpClient WithRole(this HttpClient client, string role)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", For(role));
        return client;
    }
}
