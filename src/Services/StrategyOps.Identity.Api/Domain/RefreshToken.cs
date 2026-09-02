using System.Security.Cryptography;

namespace StrategyOps.Identity.Api.Domain;

/// <summary>
/// A long-lived credential exchanged for short-lived access tokens.
/// </summary>
/// <remarks>
/// This is why access tokens can be short. A JWT cannot be revoked - once signed it is valid
/// until it expires, because validating it involves no lookup. So the access token lives 30
/// minutes and the refresh token, which IS looked up and CAN be revoked, lives days. Logging
/// someone out means deleting the refresh token and waiting at most 30 minutes.
///
/// Refresh tokens are stored hashed, for the same reason passwords are: a leaked table should
/// not hand out working credentials.
/// </remarks>
public sealed class RefreshToken
{
    private RefreshToken()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public static (RefreshToken Token, string PlainText) Issue(Guid userId, DateTimeOffset now, int lifetimeDays)
    {
        var plain = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        return (new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = HashOf(plain),
            ExpiresAtUtc = now.AddDays(lifetimeDays)
        }, plain);
    }

    public bool IsUsable(DateTimeOffset now) => RevokedAtUtc is null && ExpiresAtUtc > now;

    public void Revoke(DateTimeOffset now) => RevokedAtUtc = now;

    public static string HashOf(string plainText) =>
        Convert.ToBase64String(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(plainText)));
}
