# 17. How do you handle failures and fault tolerance in microservices?

*Asked by: Globant, Cognizant, Infosys, EPAM*

## The 60-second answer

> The starting assumption is that every network call will fail, be slow, or arrive twice — so
> the design question is not "how do I stop failures" but "what happens when this fails".
>
> I use four layers. **Timeouts** so a hung call cannot consume a thread forever. **Retries
> with backoff and jitter** for transient faults. A **circuit breaker** so a sustained failure
> stops being retried and starts failing fast. And **graceful degradation** so a failed
> dependency costs you a section of a screen rather than the whole request.
>
> Underneath that, the structural answer is **asynchronous messaging**: escalating a risk in
> StrategyOps works fine while the Issues service is down, because the message waits. You cannot
> make a synchronous call fault-tolerant — you can only make it fail better.

## The four layers

| Layer | Handles | Failure if missing |
|---|---|---|
| Timeout | slow or hung dependency | threads pile up; the caller dies of a callee's slowness |
| Retry + jitter | transient blips | a one-off packet loss becomes a user-visible error |
| Circuit breaker | sustained failure | retries hammer a struggling service and cascade |
| Fallback / degradation | anything unavailable | one section down takes the whole page down |

## Bulkheads, and the failure that kills platforms

**Each downstream service gets its own HTTP client and therefore its own circuit breaker.** A
shared breaker would let a sick KPI service trip the breaker for Risk too, turning partial
degradation into total outage.

The cascade to be able to describe: service A calls B synchronously; B slows down; A's threads
block waiting; A's thread pool exhausts; A stops answering *anything*, including requests that
never needed B. One slow service has taken down the platform. Timeouts and breakers exist
specifically to stop this.

## Degradation over failure

`GET /api/portfolio/{id}/overview` fans out to five services. If Benefits is down it returns
the project, its KPIs, its risks and its issues, with `benefits.available = false`. An
aggregation endpoint that returns 500 because one of four dependencies is unhealthy has
**multiplied the platform's failure rate by four** rather than hiding it.

## Measured, not asserted

`/chaos/fail` makes any service return 503 on demand. Against a failing Benefits service:

| Call | Elapsed | Benefits | KPI |
|---|---|---|---|
| 1 | 1038 ms | `503 Service Unavailable` | still available |
| 2–8 | 9–23 ms | `BrokenCircuitException` | still available |

The first call pays for the retries; every one after fails in ~15ms. That difference is what
the breaker buys, and KPI keeps working throughout because the breakers are per-service.

## In this repo

- [`ResilienceExtensions`](../../src/BuildingBlocks/StrategyOps.BuildingBlocks/Resilience/ResilienceExtensions.cs)
- [`ChaosEndpoints`](../../src/BuildingBlocks/StrategyOps.BuildingBlocks/Chaos/ChaosEndpoints.cs)
- [ADR 0008 — resilience](../adr/0008-resilience.md)

## Follow-up probes

**"What about failures on the async path?"**
> Different tools. MassTransit retries with backoff, then dead-letters to an `_error` queue.
> The outbox means nothing is lost between database and broker, and the inbox means a
> redelivery is harmless. The thing to monitor is the dead-letter queue — it is where messages
> go to be forgotten if nobody looks.

**"How do you test this?"**
> Chaos endpoints for the mechanism, and messaging tests for redelivery and compensation.
> Resilience code is the least-tested code in most systems because its conditions are hard to
> reproduce — so I made them a one-line curl.

**"What about the database?"**
> Connection resiliency (`EnableRetryOnFailure`), and readiness probes so a pod with a broken
> database is removed from rotation rather than serving errors. Critically, **liveness must not
> check the database** — a database blip would restart every replica you have.
