# ADR 0006 — A CQRS read model for the portfolio dashboard

**Status:** Accepted · **Date:** 2026-09-01

## Context

The portfolio director's question is simple and the architecture makes it hard:

> Show me every project with its RAG status, its open risks, its open issues and its
> benefit realisation.

Every one of those columns is owned by a different service with a different database.
There is no join to write.

Three ways to answer it:

1. **Fan out per request.** The gateway calls Projects, then for each project calls KPI,
   Risk, Issues and Benefits. Twenty projects is eighty-one HTTP calls, the page is as
   slow as the slowest service, and it is *down* whenever any one of four services is.
2. **Query the databases directly.** Abandons database-per-service, couples the dashboard
   to four teams' schemas, and turns every internal refactor into a dashboard outage.
3. **Keep a copy, updated by the events those services already publish.**

## Decision

Option 3. `StrategyOps.Reporting.Api` maintains `portfolio_scorecards` — **one flat row per
project**, projected from seventeen event types across five services.

Three properties define it:

- **It owns no truth.** Every column is a copy. The row can be deleted at any moment and
  rebuilt, and nothing is lost.
- **It is only ever read by queries and written by projections.** That separation *is* CQRS:
  the write side stays normalised and invariant-enforcing per service; the read side is
  denormalised and shaped for one screen.
- **It is eventually consistent**, lagging the source services by the outbox poll plus the
  broker hop — typically a second or two. The dashboard shows the lag on purpose, via a
  SignalR push and an "updated Ns ago" column.

`GET /reporting/portfolio` is then a single indexed SELECT that keeps working even when every
other service is offline, at the cost of showing data that may be a second stale. For a
dashboard that is a good trade; for a payment authorisation it would not be.

## Projections are upserts, never inserts

Events arrive out of order across five independent services — a KPI confirmation routinely
lands before the `ProjectDraftCreated` that "created" the project it refers to. So
`PortfolioProjection<T>` finds-or-creates the row, which makes ordering irrelevant for
everything except fields that genuinely overwrite each other.

Idempotency comes from the shared inbox (ADR 0004), because counters are exactly what a
redelivery corrupts.

## Two details worth stealing

**Carry resulting state, not just the delta.** `BenefitRealised` carries `RealisedToDate` as
well as `ActualValue`, so the projection *sets* rather than *adds*. A redelivery with a fresh
message id then cannot double-count. One small contract decision removes a whole class of bug.

**Derive counts, don't maintain them.** The first KPI projection incremented and decremented
RAG counters and got it wrong the moment a KPI recovered — it could not tell a new reading
from a moving one. It now keeps a `project_kpi_statuses` row per KPI and recomputes the
buckets every time. Counters drift silently and forever; derived values are self-healing.

That a projection needs a little state of its own is fine — it is all derived, and it is all
thrown away and rebuilt together.

## Rebuild is the answer to "what if the projection is wrong?"

A read model is a cache with a schema, so `POST /reporting/rebuild` discards and rebuilds it.
There are two honest ways to do that:

- **replay from an event store**, if you kept every event ever published; or
- **re-read current state from the owning services**, which is what this does — because this
  system has an *outbox*, not an event store. The outbox is a delivery mechanism; its rows are
  drained and marked processed, not retained as history.

Rebuild is the one place in this service that uses HTTP at all. Normal operation is entirely
event-driven. That asymmetry is the point: **async for the steady state, sync only when you
deliberately need a consistent snapshot right now.**

One unreachable service degrades one section of one row rather than failing the whole
rebuild, and the response reports exactly what could not be refreshed.

## Consequences

- The dashboard can show a project as Green for a second after it has gone Red. Anyone
  reading these numbers to make a real-time decision needs to know that.
- Seventeen projections are seventeen places a bug can hide, and a bug here is invisible
  until someone notices the numbers are wrong. Rebuild is the mitigation, and the projection
  tests are the prevention.
- This service publishes nothing and has no outbox. A read model that starts emitting its own
  events has usually stopped being a read model.
