# ADR 0009 — Correlation, structured logs, traces and metrics

**Status:** Accepted · **Date:** 2026-09-02

## Context

"The user says their project did not activate." In a monolith you open one log. Here the
request crossed the gateway, Projects, a saga, RabbitMQ, and three more services in three
other processes — and none of them wrote a line that obviously belongs with the others.

## Decision

### One correlation id, propagated over every hop

`CorrelationIdMiddleware` **adopts** an incoming `X-Correlation-Id` and only generates one if
there isn't one — otherwise a request crossing the gateway gets a fresh id at every hop and
the chain breaks exactly where it matters. It also:

- pushes the id into Serilog's `LogContext`, so **every** line written anywhere inside the
  request carries it, including from code that has never heard of correlation ids;
- writes it onto the **response**, so a user can quote the id from their network tab;
- writes it back onto the **request** headers, so a reverse proxy forwards it. Without that
  last step a *generated* id is logged at the gateway and then vanishes at the first hop —
  which is precisely the bug this system had until an end-to-end run exposed it.

Propagation is three separate mechanisms, and the third is the one people forget:

| Hop | Mechanism |
|---|---|
| Browser → gateway | client sends the header, or the gateway mints one |
| Service → service (HTTP) | `CorrelationHttpMessageHandler` |
| Service → broker → service | `CorrelationSendFilter` / `CorrelationConsumeFilter`, plus the id carried inside the event itself |

A message sits in a queue and is then handled by a different process on a thread with no
`HttpContext`. That is where most correlation chains quietly end.

### What it buys, concretely

One `grep` across six services' logs returns the whole story, in order:

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

Nine lines, six processes, two transports, one saga and one choreographed chain. That is the
answer to "how do you debug microservices?".

### Logs, traces and metrics answer different questions

Being able to say which is which is the substance behind the monitoring question:

- **Logs** — "what happened in this one request?" Only useful if structured and correlated.
- **Traces** — "where did the time go, and which hop failed?" A trace is the *shape* of one
  request across every service; log reading does not reconstruct it reliably.
- **Metrics** — "is this normal?" Rates, latency percentiles, error ratios. These are what you
  alert on, because alerting on log lines drowns you.

OpenTelemetry rather than a vendor SDK, so the instrumentation does not change when the
backend does: the same traces go to Jaeger, Tempo, Honeycomb or Application Insights by
changing an endpoint. MassTransit's activity source is included, so a trace follows a request
through the broker into a consumer in another process. Health probes are filtered out — they
fire constantly, tell you nothing, and bury the requests that matter.

### Liveness and readiness are different probes

Conflating them is a classic Kubernetes mistake with a nasty failure mode.

| | Question | On failure | May check dependencies? |
|---|---|---|---|
| `/health` (liveness) | is this process wedged? | pod is **killed** | **No** |
| `/health/ready` (readiness) | can I serve traffic now? | pod is removed from rotation | Yes |

If liveness checked the database, one database blip would restart **every replica you have**,
turning a brief degradation into an outage. So `/health` checks nothing and `/health/ready`
carries the `ready`-tagged database check. Both stay anonymous — a probe cannot present a
token, and a readiness endpoint returning 401 makes the orchestrator kill a healthy pod.

## Consequences

- Serilog's `LogContext` is async-local, so anything that loses the async context loses the
  property. Fire-and-forget work needs the id passed explicitly.
- Console logging is the default and Seq/OTLP are opt-in by configuration, so the lab runs
  with no observability infrastructure at all — and gains it by setting one endpoint.
- The correlation id is not a trace id. OpenTelemetry maintains its own W3C `traceparent`
  alongside it. Both exist because the correlation id is human-friendly and survives places
  where trace context is not propagated.
