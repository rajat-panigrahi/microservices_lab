# Testing a microservices system

> The short version for an interview: **four tiers, each answering a question the tier below
> it cannot, and none of them needing a broker or a database container to run.**
> 176 tests, ~11 seconds.

| Tier | Count | What it proves | What it costs |
|---|---|---|---|
| Domain | 91 | business rules — risk scoring, KPI banding, stage transitions, SLA targets | nothing; pure objects, ~250ms |
| Slice | 30 | the real HTTP endpoint, real EF mappings, real SQL, real auth | in-memory host + SQLite file, ~3s |
| Messaging | 50 | consumers, sagas, compensation, idempotency under redelivery | MassTransit in-memory harness, ~7s |
| Contract | 5 | no integration event changed shape by accident | reflection over the contracts assembly, ~300ms |

## Why this shape

The classic pyramid says "mostly unit tests". That advice was written for monoliths, where the
risk is in the logic. In a distributed system the logic is often the *easy* part — the failures
are in the seams: a message redelivered, a service that answers late, a projection that
double-counts, a SQL provider that will not translate a query.

So the middle of this pyramid is fatter than usual, and deliberately so. The **messaging tier
is the second-largest** because that is where this system's real risk lives.

### Tier 1 — domain tests

Pure objects, no infrastructure, written first. `Risk.Escalate` throwing when the risk has
already materialised is the reason a retried HTTP request cannot start the choreography chain
twice — that is a business rule, and it belongs here where it costs a millisecond to verify.

### Tier 2 — slice tests

`WebApplicationFactory` boots the **real service**: real routing, real model binding, real
FluentValidation, real EF mappings, real SQL, real JWT validation. Two substitutions only —
SQLite pointed at a temp file, and the outbox publisher removed so tests drain it explicitly
rather than racing a timer.

This tier exists because it catches what unit tests structurally cannot. It found:

- **SQLite cannot `ORDER BY` a `DateTimeOffset`.** Listing issues by SLA deadline returned
  500. The LINQ compiled, passed review, and would have worked against SQL Server.
- **Only an INTEGER primary key autoincrements in SQLite**, so the outbox's insertion-order
  column had to become the key.
- **A range operator on a short string throws**, and whether it did depended on the length of
  the service name — `issues-api` is two characters shorter than `projects-api`, so the bug
  appeared in one service and not the other.

Security is tested here too, with **genuinely signed JWTs** rather than a fake handler or
`Auth:Enabled=false`. Switching auth off in tests makes them pass on a configuration that is
never deployed, so a route that forgot its policy or a role-name typo would sail through. The
real pipeline lets these assert the negatives: 401 for no token, 401 for an expired one, 401
for a token signed with the wrong key, **403 — not 401 — for an authenticated user without the
role**, and 200 on `/health` for an anonymous probe.

### Tier 3 — messaging tests

MassTransit's in-memory harness is a **real bus** with an in-memory transport: consumers,
message ids, publish behaviour and the inbox filter all run exactly as they do over RabbitMQ.
What disappears is the broker — so these run in milliseconds and need no infrastructure, while
still testing the thing that actually breaks in production.

What they assert is the interesting part:

- **the same message delivered twice produces one effect** — the normal cost of at-least-once
  delivery, not an edge case;
- **two different messages describing the same fact also produce one effect**, which the inbox
  cannot catch and a domain rule must;
- the saga **activates only when all three legs confirm**, in any order;
- the saga **compensates the legs that succeeded** when one refuses;
- a leg confirming **after compensation began** is withdrawn immediately, so nothing is
  orphaned — the case everyone forgets;
- a participant **always answers**, even when it had nothing to undo. This one was a real bug:
  `WithdrawRiskRegisterConsumer` returned silently when there was no register, which would
  hang the saga until its timeout.

Saga tests use a **file-backed** SQLite database, not `:memory:`. A shared in-memory connection
is reused by every scope, so MassTransit's saga repository ends up nesting transactions on one
connection, which SQLite forbids.

### Tier 4 — contract tests

A snapshot of every integration event's shape. Renaming a property on an internal class is a
rename; renaming it on an integration event breaks every consumer still deployed with the old
version, at runtime, in someone else's service.

The failing test is the point. It does not say "you broke something" — it says **"you are
about to change a contract; was that deliberate, and is it backwards compatible?"** Updating
the snapshot is one line; the value is that it cannot happen silently in a diff nobody read.

It also enforces three rules mechanically: every contract carries `MessageId` (the inbox
depends on it) and `CorrelationId` (the trace depends on it); **no contract exposes an enum**,
because adding a value silently breaks older consumers while a string lets them default
sensibly; and every contract lives under a versioned namespace so `V2` can exist alongside
`V1` during a rollout.

This is the cheap end of consumer-driven contract testing. Pact goes further — each consumer
declares what it actually uses, so a producer learns which fields are safe to remove — at the
cost of a broker and cross-team ceremony. A snapshot needs a file and catches most real
breakages.

## What is deliberately not here

**Testcontainers.** The obvious way to test against real PostgreSQL and real RabbitMQ, and the
right answer for most teams. It needs a Docker daemon, which this environment does not have.
The honest consequence: the SQLite-specific behaviour above is tested, PostgreSQL-specific
behaviour is not, and the compose file is unverified.

**End-to-end tests as a tier.** They exist as `docs/demo-script.md`, run by hand. Automated
end-to-end tests across nine services are slow, flaky, and fail in ways that do not localise —
you learn *something* is broken, not *what*. The messaging tier already covers the
interactions; end-to-end is for confidence before a release, not for a pull request.

**Load and chaos testing at scale.** `/chaos/fail` proves the circuit breaker opens, which is
the mechanism. Whether the thresholds are right for real traffic is a question only production
traffic answers.

## Running them

```bash
dotnet test                                    # everything, ~11s, no infrastructure
dotnet test tests/StrategyOps.Domain.Tests     # fastest feedback while writing domain rules
dotnet test tests/StrategyOps.Messaging.Tests  # sagas, consumers, idempotency
```

Nothing needs RabbitMQ, PostgreSQL or Docker. That is not a compromise — a test suite you
cannot run without infrastructure is a test suite that stops being run.
