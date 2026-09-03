# 10. What is an API gateway and why is it required?

*Asked by: Cognizant, TCS, Accenture, EPAM, Capgemini*

## The 60-second answer

> An API gateway is a single entry point in front of the services. Clients talk to it, it
> routes to whoever owns the request, and it handles the concerns that would otherwise be
> duplicated in every service — authentication, rate limiting, TLS termination.
>
> It is needed because without it every client must know every service's address, handle CORS
> for each, authenticate against each, and make one round trip per service to render a screen.
> On a mobile network that last one alone is fatal.
>
> In StrategyOps the gateway is YARP. It does four things: routes `/api/*`, validates JWTs at
> the edge, rate limits per user, and aggregates — `/api/portfolio/{id}/overview` fans out to
> five services in parallel and returns one document in about 480ms, instead of the client
> making five calls.

## What belongs in it — and what does not

**Belongs:** routing, edge authentication, rate limiting, TLS termination, request/response
logging and correlation, aggregation for clients, API composition and versioning.

**Does not belong:** business rules, data access, orchestration of business workflows.

That second list is the important one. **The gateway must stay thin.** The moment business
logic moves into it, every team is blocked on changing one deployable and you have built a
distributed monolith with a fancy front door. The saga in this system deliberately lives in the
Projects service, not the gateway, for exactly that reason.

## In this repo

- Routing and config: [gateway `appsettings.json`](../../src/Gateway/StrategyOps.Gateway/appsettings.json)
- Aggregation with partial degradation: [`PortfolioOverview`](../../src/Gateway/StrategyOps.Gateway/Features/PortfolioOverview/PortfolioOverview.cs)
- Per-user token-bucket rate limiting: [`Program.cs`](../../src/Gateway/StrategyOps.Gateway/Program.cs)
- Token relay downstream: [`BearerTokenForwardingHandler`](../../src/BuildingBlocks/StrategyOps.BuildingBlocks/Auth/BearerTokenForwardingHandler.cs)

## Two details that get probed

**Aggregation must degrade, not fail.** If Benefits is down, the overview endpoint returns the
project, its KPIs, its risks and its issues, with `benefits.available = false`. An aggregation
endpoint that returns 500 because one of four dependencies is unhealthy has multiplied the
platform's failure rate by four rather than hiding it.

**The gateway is not a trust boundary.** Every service validates the JWT again, because
anything on the network that can reach a service can call it directly. Trusting a header the
gateway supposedly set is how one compromised pod becomes a compromised platform. Validation is
a signature check against an in-memory key — doing it twice costs microseconds.

## Follow-up probes

**"Isn't the gateway a single point of failure?"**
> Yes, and you run several replicas behind a load balancer — the k8s manifest has
> `replicas: 2` and a PodDisruptionBudget. It is a single point of *entry*, which is
> unavoidable; a single point of *failure* is a deployment choice.

**"Backend-for-frontend?"**
> A gateway per client type — one for mobile, one for web — because their needs differ. Mobile
> wants fewer, fatter responses; a desktop dashboard wants everything. One gateway serving both
> ends up with query parameters that mean "which client are you".

**"YARP, Ocelot, or a managed gateway?"**
> YARP for .NET, because it is fast, Microsoft-maintained and configured from `appsettings`.
> Ocelot is the older .NET option. Kong, Azure APIM or AWS API Gateway when you want a managed
> product with policy tooling — worth it if you need developer portals and quotas, overkill if
> you need routing.

**"Service mesh instead?"**
> Different job. A mesh (Istio, Linkerd) handles *service-to-service* traffic — mTLS, retries,
> traces — via sidecars, with no application code. A gateway handles *north-south* traffic from
> outside. Many systems have both; a mesh would replace my Polly policies but not the gateway.
