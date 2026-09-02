# ADR 0008 — Retry, circuit breaker, timeout

**Status:** Accepted · **Date:** 2026-09-02

## Context

Once a call leaves the process it can be slow, fail, or hang. In a monolith a method call
does none of those. Every synchronous hop in this system needs a policy for all three.

## Decision

A Polly pipeline on every outbound HTTP client, **in this order** — and the order is the part
people get wrong:

```
total timeout  →  retry (backoff + jitter)  →  circuit breaker  →  per-attempt timeout
```

1. **Total timeout** caps the whole operation. Without it, three retries against a service
   taking three seconds each means the caller waits nine seconds for a failure — and their
   caller has usually given up already.
2. **Retry** with exponential backoff **and jitter**. Jitter is not decoration: without it,
   every caller that failed at the same moment retries at the same moment, and the recovering
   service is knocked over by a synchronised thundering herd.
3. **Circuit breaker**, *inside* the retry so it observes every individual attempt. Outside,
   it would see one failure per operation and take far too long to trip.
4. **Per-attempt timeout**, innermost, so one hung request is abandoned and retried rather
   than consuming the entire budget.

**5xx is retried; 4xx is not.** A 400 or a 404 fails identically every time — retrying it
wastes the budget and multiplies load for nothing.

**Each downstream service gets its own named client and therefore its own breaker.** A shared
breaker would let a sick KPI service trip the breaker for Risk too, turning partial
degradation into total outage — precisely the opposite of the point.

## Why a breaker at all, when retries already handle failure

Retries help with a *transient* fault and actively hurt with a *sustained* one: a struggling
service receives three times the traffic exactly when it can least afford it, while callers
pile up waiting and exhaust their own thread pools. That cascade is how one slow service takes
down a platform.

The breaker's job is to **stop asking** — fail fast, give the dependency room to recover, keep
the caller responsive. A retry is optimism; a breaker is knowing when to stop being optimistic.

Retries also assume the operation is safe to repeat. Everything retried here is a GET; write
paths go through the outbox and are made safe by the inbox instead.

## Demonstrated, not asserted

`/chaos/fail` on any service makes it return 503 on demand — because resilience code is the
least-tested code in most systems, and you cannot see a breaker work by reading it.

Against a failing Benefits service, through the gateway's aggregation endpoint:

| Call | Elapsed | Benefits | KPI |
|---|---|---|---|
| 1 | 1038 ms | `503 Service Unavailable` | still available |
| 2 | 19 ms | `BrokenCircuitException` | still available |
| 3–8 | 9–23 ms | `BrokenCircuitException` | still available |

The first call pays for the retries. Every call after it fails in ~15 ms instead of ~1 s —
**that** is what the breaker buys. And KPI keeps working throughout, because the breakers are
per-service. After healing, the next probe closes the circuit within a few seconds.

The chaos endpoints are registered only outside Production, so the switch cannot be deployed
by accident.

## Consequences

- An aggregated response can be partially unavailable. `/api/portfolio/{id}/overview` returns
  `available: false` per section rather than failing the whole request — an endpoint that
  returns 500 because one of four dependencies is unhealthy has multiplied the platform's
  failure rate by four instead of hiding it.
- Timeout budgets have to be consistent down the chain: an inner timeout longer than its
  caller's is dead code, because the caller gives up first.
- None of this helps a service that is *wrong* rather than *down*. Retries and breakers handle
  availability, not correctness.
