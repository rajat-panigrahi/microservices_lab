# 18. Explain the Retry and Circuit Breaker patterns

*Asked by: Infosys, Cognizant, EPAM, Accenture, Capgemini*

## The 60-second answer

> **Retry** handles a transient fault — a dropped packet, a brief GC pause, a pod restarting.
> You try again, with exponential backoff so you do not hammer the service, and with **jitter**
> so every caller does not retry at the same instant.
>
> **Circuit breaker** handles a *sustained* fault. It watches the failure rate and, once it
> crosses a threshold, stops making calls entirely for a period — failing fast instead of
> waiting for a timeout.
>
> The key insight is that they solve **opposite** problems, and using only retry actively makes
> a sustained failure worse: a struggling service gets three times the traffic exactly when it
> can least afford it, and callers pile up waiting until their own thread pools are exhausted.
> That cascade is how one slow service takes down a platform.
>
> A retry is optimism. A breaker is knowing when to stop being optimistic.

## The circuit breaker's three states

```
        failures exceed threshold
CLOSED ──────────────────────────→ OPEN
  ↑                                  │  all calls fail instantly,
  │                                  │  no traffic to the dependency
  │  probe succeeds                  │  (break duration elapses)
  │                                  ↓
  └───────────────────── HALF-OPEN ──┘
        probe fails → back to OPEN
```

- **Closed** — normal. Calls pass through; failures are counted.
- **Open** — the dependency is considered down. Calls fail **immediately** without a network
  attempt. This is the point: the caller stays responsive and the dependency gets breathing
  room.
- **Half-open** — after the break duration, one probe is allowed. Success closes the circuit;
  failure re-opens it.

## Order matters, and it is the thing people get wrong

```
total timeout  →  retry (backoff + jitter)  →  circuit breaker  →  per-attempt timeout
```

1. **Total timeout** caps the whole operation. Without it, three retries against a service
   taking three seconds each means the caller waits nine seconds — and *their* caller has
   already given up.
2. **Retry** with exponential backoff and jitter.
3. **Circuit breaker inside the retry**, so it sees every individual attempt. Outside, it would
   see one failure per operation and take far too long to trip.
4. **Per-attempt timeout** innermost, so one hung request is abandoned and retried rather than
   consuming the entire budget.

## Two rules that get probed

**Retry 5xx, never 4xx.** A 400 or a 404 will fail identically every time; retrying wastes the
budget and multiplies load for nothing.

**Only retry what is safe to repeat.** Everything retried here is a GET. Write paths go through
the outbox and are made safe by the inbox instead — see Q19.

## One breaker per dependency

Each downstream service gets its own HTTP client and therefore its own breaker. A shared
breaker would let a sick KPI service trip the breaker for Risk too, turning partial degradation
into a total outage — the opposite of the intent.

## Measured in this repo

`POST /chaos/fail` on Benefits, then hitting the gateway's aggregation endpoint:

| Call | Elapsed | Benefits | KPI |
|---|---|---|---|
| 1 | 1038 ms | `503 Service Unavailable` | still available |
| 2 | 19 ms | `BrokenCircuitException` | still available |
| 3–8 | 9–23 ms | `BrokenCircuitException` | still available |

The first call pays for the retries. Every call after it fails in ~15 ms. After healing, the
next probe closed the circuit within a few seconds.

- [`ResilienceExtensions`](../../src/BuildingBlocks/StrategyOps.BuildingBlocks/Resilience/ResilienceExtensions.cs)
- [ADR 0008](../adr/0008-resilience.md)

## Follow-up probes

**"How do you choose the thresholds?"**
> From the dependency's normal error rate and recovery time. Mine trips at a 50% failure ratio
> over a 20-second window with a minimum of 5 calls, and breaks for 15 seconds. The minimum
> throughput matters: without it, one failure out of one call is a 100% failure rate and the
> breaker trips on a single blip.

**"What happens to the caller when the circuit is open?"**
> It gets an exception immediately — `BrokenCircuitException` in Polly — and must decide. My
> aggregation endpoint catches it and marks that section unavailable, so the user still gets
> four sections out of five.

**"Bulkhead?"**
> Isolating resources so one dependency cannot consume them all — separate connection pools or
> concurrency limits per dependency. Separate HTTP clients per service is a light version.
> Without it, calls to one slow service can starve the thread pool everything else needs.

**"Polly v7 or v8?"**
> v8 introduced resilience pipelines and `AddResilienceHandler`, replacing the older policy
> API. This repo uses v8 through `Microsoft.Extensions.Http.Resilience`.
