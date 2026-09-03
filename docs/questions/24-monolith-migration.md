# 24. How do you migrate a monolithic application to microservices?

*Asked by: Cognizant, Virtusa, Infosys*

## The 60-second answer

> Incrementally, with the **strangler fig** pattern: put a facade in front of the monolith, and
> extract one capability at a time behind it, routing that capability's traffic to the new
> service while everything else still goes to the monolith. The monolith shrinks until it is
> gone — or until what is left is small enough to leave alone, which is a perfectly good
> outcome.
>
> The thing I would say first, though, is that **most of the work is not extraction, it is
> untangling.** In my monolith, escalating a risk raises an issue, drops the project's health
> and flags the benefit — three services' worth of work, in one method, in one transaction.
> Someone has to find *that* method, and every other one like it, and turn it into a message.
>
> And I would push back on a big-bang rewrite. Rewriting while the business keeps changing
> means chasing a moving target with two systems to maintain.

## The order I would actually work in

**1. Do not extract anything yet.** Add the facade — a reverse proxy in front of the monolith
so traffic can be redirected later without clients noticing. Add logging, correlation ids and
metrics, because you cannot safely change what you cannot observe.

**2. Find the seams.** Event storming with domain experts, looking for where the *language*
changes. Look at which tables are written together, which modules change together in git
history, and which parts have different scaling or availability needs.

**3. Split the data first — logically.** This is the step people skip and the reason migrations
stall. Before extracting a service, stop the rest of the monolith joining to its tables:
introduce an internal module boundary and force access through an interface. If you cannot do
that inside one process, you certainly cannot do it across a network. **A modular monolith is
the honest halfway house**, and sometimes the right place to stop.

**4. Extract the easiest useful thing first.** Fewest inbound dependencies, clearest boundary,
lowest risk. The goal of the first extraction is to learn the mechanics — pipeline, deployment,
monitoring, on-call — not to deliver value.

**5. Route traffic gradually.** Facade sends a small percentage to the new service, compare
results, then ramp. **Keep the old code path until you are confident**, then delete it — an
extraction that leaves both paths alive forever is worse than not extracting.

**6. Repeat, and stop when it stops paying.** There is no prize for eliminating the monolith.

## Applied to this repo

`src/Monolith/` is the "before". Here is how I would cut it:

| Order | Extract | Why this order |
|---|---|---|
| 1 | **Reporting** | read-only; no writes to move, no consistency risk, and it immediately proves the event plumbing |
| 2 | **Benefits** | few inbound dependencies; finance already thinks of it separately |
| 3 | **KPI** | mostly self-contained; different cadence |
| 4 | **Risk** and **Issues** | the hard pair — the escalation method has to become an event chain |
| 5 | **Projects** | last; it is the hub, and by now it is what remains |

Reporting first is the important choice: it is the lowest-risk way to get the outbox, the
broker, the deployment pipeline and the monitoring working, before anything that writes moves.

## The specific seams to cut

In `src/Monolith/Program.cs`, three places do the untangling work:

- **`POST /projects`** — writes the project, three KPIs and a benefit profile in one
  `SaveChanges`. Becomes the initiation saga. This is the transaction that turns into
  compensation.
- **`POST /risks/{id}/escalate`** — raises an issue and changes project health inline. Becomes
  `RiskEscalated` plus two consumers. This is the method that turns into choreography.
- **`GET /portfolio`** — one query joining four tables. Becomes the read model. This is the join
  that turns into seventeen projections.

Each of those is a line-by-line reason the monolith cannot simply be split, which is exactly
why the file is in the repo.

## What you have to add that did not exist before

Nothing in this list is optional, and all of it is new work the monolith never needed:

- a transactional outbox in every service that publishes
- an inbox in every service that consumes
- compensation logic for every distributed operation
- correlation ids and distributed tracing
- a read model, plus a way to rebuild it
- contract versioning and a compatibility policy
- per-service pipelines, dashboards and on-call

That list *is* the cost of the migration. Anyone who cannot name it has not done one.

## Follow-up probes

**"How do you handle the shared database during migration?"**
> Views or an anti-corruption layer for reads while the new service still needs the old data;
> for writes, dual-write during transition and then cut over. The rule is that the *new* service
> owns its data at the end — if it is still reading the monolith's tables, you have not
> extracted anything, you have added a network hop.

**"How do you keep data in sync during the transition?"**
> Change data capture (Debezium) or dual writes with reconciliation. Both are temporary and
> both are unpleasant; the aim is to make the window short, not comfortable.

**"What if the migration stalls halfway?"**
> Very common, and it is not automatically a failure. A monolith plus three well-chosen
> services can be a perfectly good architecture. The failure mode is stalling **with a shared
> database** — that is a distributed monolith, and it is strictly worse than where you started.

**"How long does it take?"**
> Years for a large system, and it competes with feature work the whole time. If leadership
> expects a quarter, the honest answer is to reset that expectation before starting rather than
> after.
