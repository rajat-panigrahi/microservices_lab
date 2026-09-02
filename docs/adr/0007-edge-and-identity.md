# ADR 0007 — API gateway, service discovery and JWT auth

**Status:** Accepted · **Date:** 2026-09-02

## Context

Six services with six ports is fine for curl and hopeless for a client. A browser would need
to know every address, handle CORS for each, authenticate against each, and make five round
trips to render one screen.

## Decisions

### One gateway, doing four jobs

`StrategyOps.Gateway` (YARP) is the single front door on `:5100`:

1. **Routing.** `/api/projects/**` → Projects, `/api/risks/**` → Risk, and so on. Clients
   know one host.
2. **Authentication at the edge.** An anonymous request is rejected once, at the boundary,
   instead of six services each discovering it independently.
3. **Rate limiting**, keyed per user rather than globally, so one noisy client cannot spend
   everyone else's budget. A *token bucket* rather than a fixed window, because opening a
   dashboard legitimately fires several requests at once — verified: 130 rapid calls against
   a 120-token bucket returned 122×200 and 8×429.
4. **Aggregation.** `/api/portfolio/{id}/overview` fans out to five services in parallel and
   returns one document — one round trip instead of five.

The gateway must stay **thin**. The moment business rules move into it, it becomes a
distributed monolith's control centre: every team blocked on changing one deployable.
Routing, auth, rate limiting and aggregation are all it does here.

### Every service validates the token again

The obvious objection is that the gateway already checked. It is still wrong to trust that,
because **the gateway is not the only way in**. Anything on the network that can reach a
service can call it directly. Trusting a header the gateway supposedly set is how one
compromised pod becomes a compromised platform.

Validation is a signature check against a key already in memory — no network call, no
database hit. Doing it twice costs microseconds.

`SetFallbackPolicy(RequireAuthenticatedUser)` makes services **secure by default**: an
endpoint that forgets to declare a policy is closed, not open. Forgetting `[Authorize]` is
far more common than forgetting `AllowAnonymous`, and only one of those is a breach.

### Identity flows to downstream services by token relay

The first version of the aggregation endpoint authenticated the user at the edge and then
called four services anonymously — and all four correctly returned 401. Fixed with
`BearerTokenForwardingHandler`, which carries the caller's token onto outbound calls.

This matters beyond making it work: a **Viewer** calling the aggregation endpoint now gets a
Viewer's answer from every service. Authorization is not something the gateway can decide on
everyone else's behalf.

The alternative — **client credentials**, where the gateway calls downstream as itself — is
right for background work with no user in the picture, and wrong here, because it would give
every request the gateway's permissions and downstream logs would show "gateway did this"
rather than who actually did.

### HS256 today, and what production needs instead

A shared symmetric key is the shortest path to a working example, and it has two real costs
worth being able to state:

- every service needs the **signing** key just to **validate**, so any one of them could mint
  tokens for all the others;
- rotating the key means redeploying everything at once.

Production uses asymmetric signing (RS256/ES256): the provider holds the private key, services
fetch public keys from JWKS, rotation is a non-event — and in .NET that usually means Entra
ID, Duende, Auth0 or Keycloak rather than hand-rolling it.

Likewise `/connect/token` is the **password grant**, discouraged in OAuth 2.1 because the
application handles the user's actual password, which rules out MFA and federation. Real
logins use **authorization code with PKCE**; machine-to-machine uses **client credentials**.

Access tokens live 30 minutes because a JWT **cannot be revoked** — validating one involves no
lookup. Refresh tokens can be revoked, are stored hashed, and are **rotated on use**, so a
stolen one races the real user and the loser presents a revoked token: a detectable signal.

### A registry, even though Kubernetes would replace it

`StrategyOps.Discovery.Api` is a small Consul/Eureka: register, heartbeat, look up, evict.
Services self-register on startup and `DiscoveryHttpMessageHandler` rewrites
`http://projects-api/...` to a live instance — **client-side load balancing**, no extra hop.

Three details that are the actual content:

- It is a **lease**, not a registration. A config file lists what someone thinks is running; a
  registry lists what has proved it is running in the last few seconds. The interesting
  failure is not a clean shutdown, it is a process that was killed or partitioned.
- **Eviction uses a grace multiplier**, and heartbeats go out at a third of the lease.
  Evicting exactly on the boundary makes every GC pause look like a death and the registry
  flaps.
- Lookups are **cached for ten seconds**, so the registry is not on the critical path of every
  call in the system — otherwise the least reliable component is in front of everything. The
  cost is staleness, which is what the retry policy absorbs. The two are designed together.

On Kubernetes you would run none of this: a Service gives a stable DNS name, and readiness
probes play the role of heartbeats. Building it once by hand is how you learn what the
platform is doing for you.

## Consequences

- The gateway is a single point of failure. In production it runs several replicas behind a
  load balancer; the single-node registry has the same problem and real ones run as a cluster.
- Tests mint genuinely signed JWTs rather than switching auth off, so the deployed
  authentication pipeline is the one under test — which is what lets them assert 401 for an
  expired token, 401 for a token signed with the wrong key, and 403 (not 401) for an
  authenticated user without the role.
