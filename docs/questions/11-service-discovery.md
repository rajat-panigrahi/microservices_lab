# 11. What is service discovery / a service registry?

*Asked by: Cognizant, Infosys, EPAM, Capgemini*

## The 60-second answer

> Service discovery is how a service finds another service's address without hard-coding it.
> Instances come and go with every deploy, scale event and crash, so addresses cannot live in
> config.
>
> A registry is the piece that knows: services **register** on startup, **heartbeat** to keep a
> lease, and get **evicted** if they stop answering. Callers look up a logical name and get a
> list of live instances.
>
> I built a small one in StrategyOps so the mechanism is visible: services self-register, a
> `DelegatingHandler` rewrites `http://projects-api/...` to a real instance, and a background
> reaper evicts stale leases. In production I would use Consul, or — far more likely — nothing
> at all, because Kubernetes already does this with Services and DNS.

## The two shapes

**Client-side discovery** (what this repo does): the caller asks the registry and picks an
instance itself. No extra hop; the caller does its own load balancing. Every caller needs the
registry client.

**Server-side discovery**: the caller hits a load balancer or a Kubernetes Service, which
picks. Simpler callers, one more hop, and the balancer is another thing to run.

## The three details that matter

**1. It is a lease, not a registration.** A config file lists what someone *thinks* is running;
a registry lists what has *proved* it is running in the last few seconds. The interesting
failure is not a clean shutdown — it is a process that was killed or partitioned off the
network and never got to say goodbye.

**2. Heartbeat at a fraction of the lease, and evict with a grace period.** Heartbeats go out
at a third of the lease so two can be lost to a blip. Eviction uses a grace multiplier, because
evicting exactly on the boundary makes every GC pause look like a death and the registry flaps.

**3. Cache lookups.** Ten seconds here. Without a cache the registry sits on the critical path
of every call in the system and becomes the least reliable thing in it. The cost is staleness —
which is precisely what the retry policy absorbs. **The cache and the retry policy are designed
together.**

## In this repo

- Registry service: [`StrategyOps.Discovery.Api`](../../src/Services/StrategyOps.Discovery.Api)
- Client-side resolution and round-robin: [`DiscoveryHttpMessageHandler`](../../src/BuildingBlocks/StrategyOps.BuildingBlocks/Discovery/DiscoveryHttpMessageHandler.cs)
- Self-registration and heartbeat: [`ServiceRegistrationService`](../../src/BuildingBlocks/StrategyOps.BuildingBlocks/Discovery/ServiceRegistrationService.cs)

Verified live: six services self-registered, and `GET /registry/services` listed all six.

## Follow-up probes

**"Do you need this on Kubernetes?"**
> No — and I would delete it. A Service gives you a stable DNS name and load balances across
> pods, and readiness probes play the role of heartbeats. The k8s manifest in this repo sets
> `Discovery__Enabled: "false"` for exactly that reason. Building it once by hand is how you
> learn what the platform is doing for you.

**"Isn't the registry itself a single point of failure?"**
> Yes, and mine is a single node. Real registries run as a cluster with a consensus protocol —
> Consul uses Raft — precisely because of this. The ten-second cache also means a registry
> outage is survivable for ten seconds, and callers fall back to configured addresses.

**"What happens when the registry restarts?"**
> Every instance re-registers within one heartbeat interval. Until then lookups return empty
> and callers fall back. The subtle bug I had to fix: if a heartbeat 404s — because the registry
> restarted, or the instance was evicted during a long GC pause — the service must
> **re-register**, not keep heartbeating into a registry that has never heard of it.
