namespace StrategyOps.BuildingBlocks.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "https://strategyops.local/identity";

    public string Audience { get; set; } = "strategyops-api";

    /// <summary>
    /// The symmetric signing key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// HS256 with a shared secret is used here because it is the shortest path to a working,
    /// readable example - but be clear about what it costs, because this is exactly what an
    /// interviewer will probe:
    /// </para>
    /// <list type="bullet">
    ///   <item>every service needs the <b>signing</b> key just to <b>validate</b> a token, so
    ///   any one of them could mint tokens for all the others. There is no separation between
    ///   issuer and verifier.</item>
    ///   <item>rotating the key means redeploying everything at once.</item>
    /// </list>
    /// <para>
    /// Production uses asymmetric signing (RS256/ES256): the identity provider holds the
    /// private key, services fetch public keys from a JWKS endpoint, and rotation is a
    /// non-event. In .NET that usually means Microsoft Entra ID, Duende IdentityServer, Auth0
    /// or Keycloak rather than hand-rolling this at all.
    /// </para>
    /// <para>
    /// The key is read from configuration precisely so it can come from a secret store rather
    /// than source control.
    /// </para>
    /// </remarks>
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 30;

    public int RefreshTokenDays { get; set; } = 7;

    /// <summary>Turns the whole scheme off. Only ever set false for a local walkthrough.</summary>
    public bool Enabled { get; set; } = true;
}
