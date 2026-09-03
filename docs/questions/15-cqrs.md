# 15. What is CQRS and why / when would you use it?

*Asked by: EPAM, Deloitte, Publicis Sapient, Accenture*

## The 60-second answer

> CQRS is separating the model you write with from the model you read with. The write side
> enforces invariants and is normalised; the read side is shaped for how the data is actually
> queried and is usually denormalised.
>
> The reason to use it in microservices is blunt: **the query you need spans services, so there
> is no join to write.** In StrategyOps the portfolio director wants every project with its RAG,
> its open risks, its open issues and its benefit realisation. Those four columns live in four
> databases owned by four services.
>
> So the Reporting service keeps one flat row per project, projected from seventeen event types
> across five services. `GET /reporting/portfolio` is then one indexed SELECT that keeps working
> even when every other service is offline — at the cost of being a second or two stale.

## The alternatives it beats

1. **Fan out per request.** Twenty projects means eighty-one HTTP calls, the page is as slow as
   the slowest service, and it is *down* whenever any one of four services is.
2. **Query the databases directly.** Abandons database-per-service, couples the dashboard to
   four teams' schemas, and turns every internal refactor into a dashboard outage.
3. **Keep a copy, updated by events.** This.

## Three properties that define the read model

- **It owns no truth.** Every column is a copy. It can be deleted at any moment and rebuilt.
- **Written only by projections, read only by queries.** That separation *is* CQRS.
- **Eventually consistent**, lagging by the outbox poll plus the broker hop. The dashboard shows
  the lag deliberately, with a SignalR push and an "updated Ns ago" column.

## Two implementation details worth stealing

**Carry resulting state, not just the delta.** `BenefitRealised` carries `RealisedToDate` as
well as `ActualValue`, so the projection *sets* rather than *adds*. A redelivery with a fresh
message id then cannot double-count. One small contract decision removes a whole class of bug.

**Derive counts, do not maintain them.** My first KPI projection incremented and decremented RAG
counters and got it wrong the moment a KPI recovered — it could not tell a new reading from a
moving one. It now keeps a per-KPI status row and recomputes the buckets every time. Counters
drift silently and forever; derived values are self-healing.

## Projections upsert, never insert

Events arrive out of order across five independent services — a KPI confirmation routinely
lands before the `ProjectDraftCreated` that "created" the project. So every projection
finds-or-creates the row, which makes ordering irrelevant for everything except fields that
genuinely overwrite each other.

## Rebuild is the answer to "what if the projection is wrong?"

A read model is a cache with a schema. `POST /reporting/rebuild` discards and rebuilds it. Two
honest ways to do that: **replay from an event store**, if you kept every event; or **re-read
current state from the owning services**, which is what this does — because this system has an
*outbox*, not an event store. The outbox is a delivery mechanism; its rows are drained and
marked processed, not retained as history.

Verified: a deliberately corrupted row (issues 99, forecast 1, no KPIs) was repaired in 0.5s.

## In this repo

- Read model: [`PortfolioScorecard`](../../src/Services/StrategyOps.Reporting.Api/Domain/PortfolioScorecard.cs)
- Projections: [`Features/Projections/`](../../src/Services/StrategyOps.Reporting.Api/Features/Projections)
- Rebuild: [`RebuildReadModel`](../../src/Services/StrategyOps.Reporting.Api/Features/RebuildReadModel/RebuildReadModel.cs)

## Follow-up probes

**"Is CQRS the same as event sourcing?"**
> No, and conflating them is the most common mistake. **CQRS is separating read and write
> models. Event sourcing is storing state as a sequence of events instead of current state.**
> They pair well — event sourcing gives you a natural way to build projections — but each works
> without the other. This system is CQRS **without** event sourcing: services store current
> state in normal tables, and the read model is built from integration events.

**"When would you not use it?"**
> When the read and write models are the same shape, which is most CRUD. CQRS costs you a second
> model, a synchronisation mechanism, and a staleness window. Inside a single service where one
> query answers the question, do not.

**"How stale is too stale?"**
> A business question. A second is fine for a portfolio dashboard and unacceptable for a trading
> position. If the answer is "it must never be stale", you cannot use a read model — which
> usually means those two things belonged in one service.
