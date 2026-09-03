# 23. How do you implement logging and monitoring in microservices?

*Asked by: Infosys, EPAM, Cognizant, Accenture*

## The 60-second answer

> Three things, answering three different questions. **Structured logs** answer "what happened
> in this one request?". **Traces** answer "where did the time go, and which hop failed?".
> **Metrics** answer "is this normal?" — and metrics are what you alert on, because alerting on
> log lines drowns you.
>
> The thing that makes all of it usable is a **correlation id** propagated across every hop and
> stamped on every log line. Without it, "the user says their project did not activate" means
> reading six services' logs and guessing which lines belong together.
>
> In StrategyOps one id flows from the gateway through HTTP, into a saga, over RabbitMQ, into
> consumers in three other processes. One grep returns the whole story, in order.

## The three pillars, concretely

| | Answers | Tooling here | Alert on it? |
|---|---|---|---|
| **Logs** | what happened in this request | Serilog → console / Seq | no — too noisy |
| **Traces** | where the time went, which hop failed | OpenTelemetry → Jaeger | no |
| **Metrics** | is this normal? | OpenTelemetry → Prometheus | **yes** |

**Structured, not string-formatted.** `logger.LogInformation("Project {ProjectCode} activated", code)`
keeps `ProjectCode` as a queryable field. `$"Project {code} activated"` produces text you can
only grep. At nine services, greppable text is not enough.

## The correlation id, and the three hops

- **Adopted, not always generated.** If the caller sent one, that one wins — otherwise a request
  crossing the gateway gets a fresh id at every hop and the chain breaks where it matters.
- Pushed into Serilog's `LogContext`, so **every** line inside the request carries it, including
  from code that has never heard of correlation ids.
- Returned on the **response**, so a user can quote it from their network tab.
- Written back onto the **request** headers, so a reverse proxy forwards it. Skipping that step
  was a real bug here: a *generated* id was logged at the gateway and then vanished at the
  first hop.

| Hop | Mechanism |
|---|---|
| Browser → gateway | client header, or the gateway mints one |
| Service → service | `CorrelationHttpMessageHandler` |
| Service → broker → service | MassTransit send/consume filters + the id inside the event |

**The broker hop is the one people forget.** A message sits in a queue and is then handled by a
different process on a thread with no `HttpContext`.

## What it produces

One grep across six services:

```
gateway       HTTP POST /api/projects/…/submit-for-initiation responded 200 in 98ms
projects-api  HTTP POST /projects/…/submit-for-initiation responded 200 in 91ms
benefits-api  Registered a 280,000 benefit forecast for PRJ-0099
kpi-api       Provisioned a scorecard with 3 baseline KPIs for PRJ-0099
projects-api  Project PRJ-0099 activated: all three legs provisioned
risk-api      HTTP POST /risks/…/escalate responded 200 in 43ms
issues-api    Raised Critical issue … from escalated risk …
projects-api  Project PRJ-0099 moved to Red because a Critical issue was raised
benefits-api  Benefit forecast for PRJ-0099 flagged at risk
```

Six processes, two transports, one saga and one choreographed chain — in order.

## Why OpenTelemetry rather than a vendor SDK

The instrumentation should not change when the backend does. The same traces go to Jaeger,
Tempo, Honeycomb or Application Insights by changing an endpoint. MassTransit's activity source
is included, so a trace follows a request through the broker into a consumer in another
process.

Health probes are filtered out of tracing: they fire constantly, tell you nothing, and bury the
requests that matter in noise you pay to store.

## What to actually alert on

Not "an error was logged". The four **golden signals**: latency, traffic, errors, saturation.
For this system specifically:

- error **rate** crossing a threshold, not an individual error
- p99 latency per endpoint
- **queue depth and dead-letter count** — a queue quietly growing at 3am is much harder to
  notice than a 500
- circuit breakers open
- **outbox rows unprocessed and ageing** — the earliest signal that publishing has stalled
- saga instances stuck in a non-terminal state

The last three are microservices-specific and are the ones people forget.

## In this repo

- [`ObservabilityExtensions`](../../src/BuildingBlocks/StrategyOps.BuildingBlocks/Observability/ObservabilityExtensions.cs)
- [`CorrelationIdMiddleware`](../../src/BuildingBlocks/StrategyOps.BuildingBlocks/Correlation/CorrelationIdMiddleware.cs), [`CorrelationFilters`](../../src/BuildingBlocks/StrategyOps.BuildingBlocks/Messaging/CorrelationFilters.cs)
- [ADR 0009](../adr/0009-observability.md)

## Follow-up probes

**"Correlation id or trace id?"**
> Both, and they are different. OpenTelemetry maintains a W3C `traceparent` for tracing tools.
> The correlation id is human-friendly, survives places where trace context is not propagated,
> and is something a user can read off a screen and quote to support.

**"How do you aggregate logs?"**
> Ship them off the machine — ELK, Loki, Seq, or a cloud provider's log service. Logs on a pod's
> filesystem are gone when the pod is. Seq is wired in here behind a config switch.

**"What about sampling?"**
> Necessary at volume — tracing every request is expensive. Head-based sampling decides up
> front; tail-based samples after the fact so you can keep all the *slow* and *failed* traces,
> which are the ones you actually want.

**"How do you debug a problem in production?"**
> Start with metrics — what is abnormal. Then traces — which service and which hop. Then logs
> for that correlation id — what exactly happened. Going straight to logs is how you spend an
> hour reading the wrong service.
