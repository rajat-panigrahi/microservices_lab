# 20. How do you secure / authenticate microservices? Explain JWT and OAuth

*Asked by: Cognizant, Hexaware, Accenture, Deloitte*

## The 60-second answer

> Authentication happens once, at an identity provider, which issues a **JWT**. The gateway
> validates it at the edge, and — importantly — **every service validates it again**, because
> anything on the network that can reach a service can call it directly. Trusting a header the
> gateway supposedly set is how one compromised pod becomes a compromised platform.
>
> A JWT is a signed, base64 token carrying claims. Validating it is a **signature check against
> a key already in memory** — no network call, no database hit — which is what makes it work at
> scale, and also what makes it impossible to revoke. So access tokens are short-lived (30
> minutes here) and paired with a refresh token that *can* be revoked.
>
> **OAuth 2.0 is authorisation delegation; OpenID Connect is the authentication layer on top of
> it.** OAuth answers "may this application act on the user's behalf"; OIDC adds "and here is
> who the user is", as an ID token.

## JWT: what it actually is

Three base64 segments: header, payload, signature.

```
eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJyaXNrLm93bmVyIiwicm9sZSI6...
└── header ──────┘ └── payload (claims) ─────────────────┘ └ signature
```

**Signed, not encrypted.** Anyone who intercepts it can read every claim. So a token is not a
place to cache the user's profile — it goes on every request, it cannot be revoked, and
everything in it is public to anyone holding it. Mine carries `sub`, name, role and `jti`, and
nothing else.

## The grant types, and which to use

| Grant | Use it for | Notes |
|---|---|---|
| **Authorization code + PKCE** | any user login | the correct default; the app never sees the password |
| **Client credentials** | service-to-service, no user | a service authenticating as itself |
| **Refresh token** | renewing an access token | rotate it on use |
| **Password (ROPC)** | ~nothing | **discouraged in OAuth 2.1** |

My `/connect/token` is the password grant, and I would say so unprompted: it is there because
it is the shortest thing to demonstrate with curl. It is wrong for a real login because the
application handles the user's actual password, which rules out MFA, federation and consent
screens.

## Identity between services

When the gateway's aggregation endpoint calls four services, the caller's identity has to
travel with it. **Token relay**: the same user token is forwarded, so downstream services see
the real caller and their real roles — a Viewer gets a Viewer's answer from every service.

The first version of my aggregation endpoint authenticated at the edge and then called four
services anonymously; all four correctly returned 401. The alternative, **client credentials**,
is right for background work with no user in the picture, and wrong here — it would give every
request the gateway's permissions and make downstream logs say "gateway did this".

## Where this repo is deliberately not production-grade

**HS256 with a shared symmetric key.** Two real costs:

- every service needs the **signing** key just to **validate**, so any one of them could mint
  tokens for all the others — no separation between issuer and verifier;
- rotating the key means redeploying everything at once.

Production uses asymmetric signing (RS256/ES256): the provider holds the private key, services
fetch public keys from a JWKS endpoint, rotation is a non-event. In .NET that usually means
Entra ID, Duende IdentityServer, Auth0 or Keycloak rather than hand-rolling any of this.

## Other layers that matter

- **Secure by default**: a fallback policy of `RequireAuthenticatedUser`, so an endpoint that
  forgets to declare a policy is closed, not open. Forgetting `[Authorize]` is far more common
  than forgetting `AllowAnonymous`, and only one of those is a breach.
- **Passwords**: PBKDF2 with a per-user salt and 100k iterations, compared in fixed time. Never
  a bare SHA-256 — a GPU tries billions of those per second against a stolen table.
- **Refresh token rotation**: the old one is revoked on use, so a thief and the real user race
  and the loser presents a revoked token — a detectable signal.
- **Roles as policies**, not inline role lists, so adding a role changes one definition rather
  than thirty endpoints.
- **mTLS or a service mesh** for transport-level service identity, which I did not do here.

## In this repo

- [`Identity.Api`](../../src/Services/StrategyOps.Identity.Api), [`AuthExtensions`](../../src/BuildingBlocks/StrategyOps.BuildingBlocks/Auth/AuthExtensions.cs), [`BearerTokenForwardingHandler`](../../src/BuildingBlocks/StrategyOps.BuildingBlocks/Auth/BearerTokenForwardingHandler.cs)
- Security asserted as behaviour: [`AuthorizationTests`](../../tests/StrategyOps.Slice.Tests/Security/AuthorizationTests.cs) — 401 anonymous, 401 expired, 401 wrong signing key, **403 not 401** for an authenticated user missing the role
- [ADR 0007](../adr/0007-edge-and-identity.md)

## Follow-up probes

**"How do you revoke a JWT?"**
> You cannot, and that is the trade for stateless validation. You shorten its life and revoke
> the refresh token; the user is out within one access-token lifetime. If you need instant
> revocation you need a denylist checked on every request — at which point you have given up
> the statelessness you chose JWTs for.

**"401 or 403?"**
> 401 means "I do not know who you are" — no token, expired, bad signature. 403 means "I know
> who you are and you may not". Returning 401 for an authorisation failure tells an
> authenticated user to log in again, which in a browser is an infinite loop.

**"Why validate in every service if the gateway did?"**
> The gateway is not the only way in. Anything inside the network can call a service directly —
> another service, a debugging shell, an attacker past the edge. "Inside the cluster" is not a
> trust boundary. And it costs microseconds.
