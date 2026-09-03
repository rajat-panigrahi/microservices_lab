# Start here

You have solid C# and no microservices experience. This repo is built so that in about ten
sessions you can answer any of the 25 questions with *"here is a system I built, here is the
file, here is what bit us"* — which survives a follow-up probe in a way that reciting
definitions does not.

## How to use it

**Do not read the docs first.** Run the system, break it, then read why it is built that way.
The `docs/questions/` files are much more useful once you have seen the thing they describe
actually happen.

## Day 1 — see it work

```bash
sudo apt-get install -y dotnet-sdk-8.0 rabbitmq-server   # or use your own
dotnet build && dotnet test          # 176 tests, ~11s, no infrastructure needed
deploy/local/run-all.sh
```

Then walk [`demo-script.md`](demo-script.md) top to bottom. It takes about twenty minutes and
you will watch: a saga complete, a saga compensate, a risk escalation ripple through four
services, a circuit breaker open, and one correlation id reconstruct a whole request across six
processes.

Open **http://localhost:5106/** on one screen and escalate a risk on another. The delay before
the row turns red *is* eventual consistency. Seeing it beats any explanation of it.

## Day 2 — the shape of one service

Read [`StrategyOps.Risk.Api`](../src/Services/StrategyOps.Risk.Api) end to end. It is the
smallest complete service.

- `Domain/Risk.cs` — the aggregate. Every rule lives here, and every illegal transition throws.
- `Features/RaiseRisk/` — one vertical slice: command, validator, handler, endpoint, one file.
- `Features/EscalateRisk/` — the slice that starts the choreographed chain.
- `Infrastructure/RiskDbContext.cs` — its own database, with the outbox and inbox tables.

Then read [`ADR 0002`](adr/0002-vertical-slices.md) for why it is organised by feature rather
than by layer.

Questions covered: **1, 2, 7**.

## Day 3 — how services talk

Follow one event all the way through:

1. [`EscalateRisk`](../src/Services/StrategyOps.Risk.Api/Features/EscalateRisk/EscalateRisk.cs) — writes the risk and the outbox row in one `SaveChanges`
2. [`OutboxProcessor`](../src/BuildingBlocks/StrategyOps.BuildingBlocks/Outbox/OutboxProcessor.cs) — drains it onto RabbitMQ
3. [`RaiseIssueOnRiskEscalatedConsumer`](../src/Services/StrategyOps.Issues.Api/Features/Consumers/RaiseIssueOnRiskEscalatedConsumer.cs) — reacts, in another process

Then open the RabbitMQ management UI at http://localhost:15672 (guest/guest) and watch the
queues while you escalate a risk.

Questions: **3, 4, 5, 16, 19**. ADRs [0003](adr/0003-transactional-outbox.md) and
[0004](adr/0004-idempotent-consumers.md).

## Day 4 — the saga

[`ProjectInitiationSaga`](../src/Services/StrategyOps.Projects.Api/Features/Sagas/ProjectInitiationSaga.cs)
is the hardest and most valuable file in the repo. Read it alongside its
[ten tests](../tests/StrategyOps.Messaging.Tests/Saga/ProjectInitiationSagaTests.cs), which
cover all three failure modes.

Then run the compensation demo (step 5 of the demo script) and watch three services undo their
work.

Questions: **12, 13, 14**. [ADR 0005](adr/0005-saga-orchestration-and-choreography.md).

## Day 5 — CQRS and the read model

[`Reporting.Api`](../src/Services/StrategyOps.Reporting.Api). Corrupt the database by hand, call
`POST /reporting/rebuild`, and watch it repair itself. That single exercise is the answer to
"what happens when your projection is wrong?".

Questions: **15, 16**. [ADR 0006](adr/0006-cqrs-read-model.md).

## Day 6 — the edge

[`Gateway`](../src/Gateway/StrategyOps.Gateway) and
[`Identity.Api`](../src/Services/StrategyOps.Identity.Api). Run the chaos demo and watch the
breaker open.

Questions: **10, 11, 17, 18, 20**. ADRs [0007](adr/0007-edge-and-identity.md) and
[0008](adr/0008-resilience.md).

## Day 7 — operating it

[`ObservabilityExtensions`](../src/BuildingBlocks/StrategyOps.BuildingBlocks/Observability/ObservabilityExtensions.cs)
and the correlation plumbing. Do the one-grep exercise from the demo script.

Questions: **23**. [ADR 0009](adr/0009-observability.md), and [`testing.md`](testing.md).

## Day 8 — the monolith

Read [`src/Monolith/Program.cs`](../src/Monolith/StrategyOps.Monolith/Program.cs) — the whole
domain in ~250 lines. Compare `POST /projects` there with the saga, and
`POST /risks/{id}/escalate` with the choreographed chain.

This is the day the trade-off stops being theoretical. **One `SaveChanges` versus ~500 lines of
saga.** Be able to say what you bought for that.

Questions: **2, 6, 24**.

## Day 9 — deployment

[`deploy/`](../deploy). Build the images and run compose on your own machine — those artifacts
are the one part of this repo that was never executed, so expect to fix something, and treat
fixing it as part of the exercise.

Questions: **21, 22**.

## Day 10 — rehearse

Read all 25 [`questions/`](questions) files. Each has a spoken 60-second answer, links to the
code, follow-up probes, and the traps.

Then say them out loud. The gap between "I understand this" and "I can explain this in 60
seconds under pressure" is entirely closed by practice, and not at all by re-reading.

---

## What to lead with in an interview

Three things in this system are unusual enough to be worth volunteering:

1. **Two distributed flows, built deliberately differently** — one orchestrated, one
   choreographed — so you can compare them from experience rather than from a blog post.
2. **The outbox and inbox**, which most sample projects skip, and which are the actual answer
   to "how do you not lose or double-process a message".
3. **Seven bugs found by running the system rather than by tests** — see
   [question 25](questions/25-challenges-faced.md). Specific war stories are far more
   convincing than "distributed systems are hard".

## Optional background — how this was built

[`00-plan.md`](00-plan.md) is the design record: the plan written before any code existed,
with a section at the end recording what it got right, where it was wrong, and what was
verified by running versus only written. Not part of the ten sessions — read it if you want
the reasoning behind the shape of the repo, or if an interviewer asks how you'd approach
building something like this.

## Being honest about what this is

It is a lab, not a production system. The deliberate simplifications are listed at the end of
[`architecture.md`](architecture.md) — SQLite, a shared HS256 key, the password grant, an
in-memory scheduler. Knowing *and volunteering* the difference between what you built and what
production needs is a strong signal. Pretending there is no difference is a weak one.
