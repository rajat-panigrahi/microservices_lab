using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace StrategyOps.BuildingBlocks.Auth;

public static class AuthExtensions
{
    /// <summary>
    /// Adds JWT validation and the shared policies to a service.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every service validates the token itself, even though the gateway already did. That is
    /// deliberate and it is the answer to "isn't checking twice wasteful?" - no, because the
    /// gateway is not the only way in. Anything on the network that can reach the service can
    /// call it directly: another service, a debugging shell, an attacker who got past the
    /// edge. Trusting a header the gateway supposedly set is how one compromised pod becomes
    /// a compromised platform.
    /// </para>
    /// <para>
    /// Validation is a signature check against a key already in memory - no network call, no
    /// database hit. The cost of doing it twice is microseconds.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddStrategyOpsAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        if (!options.Enabled)
        {
            // Still register the services so [Authorize] does not throw; just let everything through.
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
            services.AddAuthorizationBuilder().SetFallbackPolicy(null);
            return services;
        }

        if (string.IsNullOrWhiteSpace(options.SigningKey) || options.SigningKey.Length < 32)
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey must be configured and at least 32 characters. A short key makes HS256 brute-forceable.");
        }

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(bearer =>
            {
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Issuer,
                    ValidateAudience = true,
                    ValidAudience = options.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
                    ValidateLifetime = true,

                    // Default is five minutes, which means an "expired" token keeps working
                    // for five more. Fine for humans, surprising in a test, and too generous
                    // for a 30-minute token.
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.ManagePortfolio, policy => policy.RequireRole(Roles.PortfolioDirector))
            .AddPolicy(Policies.ManageDelivery, policy => policy.RequireRole(Roles.PortfolioDirector, Roles.ProjectManager))
            .AddPolicy(Policies.ManageRisk, policy => policy.RequireRole(Roles.PortfolioDirector, Roles.ProjectManager, Roles.RiskOwner))
            .AddPolicy(Policies.Read, policy => policy.RequireAuthenticatedUser())

            // Secure by default: an endpoint that forgets to say what it needs is closed, not
            // open. Forgetting [Authorize] is a much more common mistake than forgetting
            // AllowAnonymous, and only one of those mistakes is a breach.
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return services;
    }
}
