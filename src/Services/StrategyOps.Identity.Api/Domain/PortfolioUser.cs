using System.Security.Cryptography;
using System.Text;
using StrategyOps.BuildingBlocks.Domain;

namespace StrategyOps.Identity.Api.Domain;

/// <summary>
/// An account in the portfolio office.
/// </summary>
/// <remarks>
/// Passwords are stored as PBKDF2 hashes with a per-user salt - never plaintext, never a bare
/// SHA-256. A fast hash is the wrong tool for passwords precisely because it is fast: a GPU
/// will try billions of SHA-256 guesses a second against a stolen table. PBKDF2 with a high
/// iteration count makes each guess expensive. Argon2id is the better modern choice; PBKDF2
/// is used here because it ships in the BCL with no extra dependency.
/// </remarks>
public sealed class PortfolioUser
{
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int Iterations = 100_000;

    private PortfolioUser()
    {
    }

    public Guid Id { get; private set; }

    public string UserName { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string Role { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public string PasswordSalt { get; private set; } = string.Empty;

    public bool IsDisabled { get; private set; }

    public static PortfolioUser Create(string userName, string displayName, string role, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);

        return new PortfolioUser
        {
            Id = Guid.NewGuid(),
            UserName = Guard.AgainstBlank(userName, "user.username_required", "A user needs a username.").ToLowerInvariant(),
            DisplayName = Guard.AgainstBlank(displayName, "user.display_name_required", "A user needs a display name."),
            Role = Guard.AgainstBlank(role, "user.role_required", "A user needs a role."),
            PasswordSalt = Convert.ToBase64String(salt),
            PasswordHash = Convert.ToBase64String(Hash(password, salt))
        };
    }

    public bool PasswordMatches(string password)
    {
        if (IsDisabled)
        {
            return false;
        }

        var salt = Convert.FromBase64String(PasswordSalt);
        var expected = Convert.FromBase64String(PasswordHash);

        // Fixed-time comparison: a normal byte-by-byte compare returns faster on an early
        // mismatch, which leaks how much of the hash was correct.
        return CryptographicOperations.FixedTimeEquals(Hash(password, salt), expected);
    }

    public void Disable() => IsDisabled = true;

    private static byte[] Hash(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password ?? string.Empty),
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashBytes);
}
