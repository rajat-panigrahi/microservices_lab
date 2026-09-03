# StrategyOps — a .NET microservices lab you can actually run

A small but enterprise-shaped **strategic portfolio management** system, built as a hands-on
answer to the 25 microservices questions that come up in .NET interviews.

Strategic Objectives are delivered by **Projects**, measured by **KPIs**, threatened by
**Risks** — which materialise into **Issues** — and justified by **Benefits**.

That domain is not decoration. It produces the three problems microservices interviews are
really about:

| The domain creates | The pattern it forces |
|---|---|
| Initiating a project touches Projects, KPI, Risk and Benefits at once | distributed transaction → **saga with compensation** |
| A risk materialises → an issue is raised → project health drops → a benefit is at risk | **event choreography** across four services |
| One portfolio dashboard reading across five databases | **CQRS** read model + **eventual consistency** |

> **New here?** Start with [`docs/00-start-here.md`](docs/00-start-here.md) — a ten-session
> path through the code. Run it before you read about it.

---

## What's in it

**Nine services** behind a YARP gateway, each owning its own database, talking over RabbitMQ.

```
Gateway (YARP, :5100) ──┬─ Projects  :5101   Identity  :5107
   routing · edge JWT   ├─ KPI       :5102   Discovery :5108
   rate limit · fan-out ├─ Risk      :5103
                        ├─ Issues    :5104
                        ├─ Benefits  :5105
                        └─ Reporting :5106   CQRS read model + live dashboard
                                  all ↕ RabbitMQ
```

**Two distributed flows, deliberately built differently** so they can be compared:

- **Project initiation — orchestrated.** A MassTransit saga sends three commands in parallel
  and either activates the project or compensates the legs that succeeded. Handles all three
  failure modes: a leg refusing, a leg never answering, and a leg answering *late*, after
  compensation began.
- **Risk escalation — choreographed.** One event, four independent reactions, no coordinator.

**Plus the machinery that makes it survive contact with reality:** a transactional outbox in
every service, an inbox for idempotent consumers, Polly retry/circuit-breaker/timeout per
dependency, JWT auth validated at the edge *and* in every service, a service registry with
client-side load balancing, correlation ids across HTTP *and* the broker, and OpenTelemetry.

---

## Run it

```bash
sudo apt-get install -y dotnet-sdk-8.0 rabbitmq-server   # or bring your own

dotnet build
dotnet test                  # 176 tests, ~11s, no infrastructure required

deploy/local/run-all.sh      # all nine services; Ctrl-C or deploy/local/stop-all.sh
```

Then walk [`docs/demo-script.md`](docs/demo-script.md) — about twenty minutes to see every
pattern actually happen. Swagger is on `/swagger` for each service; the live dashboard is at
**http://localhost:5106/**.

Demo accounts (all `Passw0rd!`): `portfolio.director`, `project.manager`, `risk.owner`, `viewer`.

---

## What was verified, and what wasn't

Being specific about this is the point of the repo.

**Verified by running the system**, not just by tests:

- a £250k project initiates in ~3s — scorecard, risk register and benefit profile all created
- **RabbitMQ died mid-run and nothing was lost**: writes kept succeeding, two events sat in the
  outbox, and the saga completed **1 second** after the broker came back. That was an accident
  of the environment, and the best evidence in the repo that the outbox works
- a £900k project is refused over the portfolio ceiling; the saga rolls the other two legs back
  in 2s, leaving 404s and a recorded reason
- escalating a Critical risk auto-raises an issue, takes the project Red and flags the benefit
  at risk — with no coordinator; resolving that issue closes the originating risk
- the read model assembles five services into one row in ~3s; a deliberately corrupted row
  (issues 99, forecast 1, no KPIs) is repaired by `POST /reporting/rebuild` in 0.42s
- 401 anonymous, **403** (not 401) for a Viewer, 200 for a director; 401 for an expired token
  and for one signed with the wrong key
- six services self-registered with the registry; gateway aggregation returned all five
  sections in 479ms
- the circuit breaker opened after one failure, cutting latency from ~1100ms to ~10ms **while
  KPI stayed healthy**, and closed again 16s after the dependency healed; rate limiting
  returned 117×200 and 13×429 for 130 rapid calls
- **one correlation id appeared in all six services' logs**, and a single grep reconstructed
  the whole request in order, across HTTP and RabbitMQ

**Not verified — no Docker daemon and no cluster in the build environment:**

- the container images (`deploy/docker/Dockerfile`, `deploy/build-images.sh`)
- `deploy/docker-compose.yml`
- `deploy/k8s/strategyops.yaml`

Both YAML files parse and were reviewed by eye, but expect to fix something on first run. That
is called out in the files themselves too.

---

## Tests

| Tier | Count | Proves |
|---|---|---|
| Domain | 91 | risk scoring, KPI banding, stage transitions, SLA targets |
| Slice | 30 | real endpoints, real EF mappings, real SQL, real JWT validation |
| Messaging | 50 | consumers, saga, compensation, idempotency under redelivery |
| Contract | 5 | no integration event changed shape by accident |

**Nothing needs RabbitMQ, PostgreSQL or Docker.** A test suite you cannot run without
infrastructure is a test suite that stops being run. See [`docs/testing.md`](docs/testing.md).

---

## Documentation

| | |
|---|---|
| [`docs/00-start-here.md`](docs/00-start-here.md) | ten-session reading path — **begin here** |
| [`docs/architecture.md`](docs/architecture.md) | context map, saga and choreography sequences, deliberate simplifications |
| [`docs/questions/`](docs/questions) | the 25 interview answers, each linked to the code |
| [`docs/adr/`](docs/adr) | nine decision records, each with its trade-off |
| [`docs/testing.md`](docs/testing.md) | the four tiers, and why the middle is unusually fat |
| [`docs/demo-script.md`](docs/demo-script.md) | copy-pasteable walkthrough of every pattern |

### The 25 questions

| | | |
|---|---|---|
| [1 What are microservices](docs/questions/01-what-are-microservices.md) | [10 API gateway](docs/questions/10-api-gateway.md) | [19 Idempotency](docs/questions/19-idempotency.md) |
| [2 Monolith vs microservices](docs/questions/02-monolith-vs-microservices.md) | [11 Service discovery](docs/questions/11-service-discovery.md) | [20 Security, JWT, OAuth](docs/questions/20-security-jwt-oauth.md) |
| [3 How they communicate](docs/questions/03-how-do-microservices-communicate.md) | [12 Distributed transactions](docs/questions/12-distributed-transactions.md) | [21 Deployment](docs/questions/21-deployment.md) |
| [4 Sync vs async](docs/questions/04-sync-vs-async.md) | [13 Saga pattern](docs/questions/13-saga-pattern.md) | [22 Docker and Kubernetes](docs/questions/22-docker-and-kubernetes.md) |
| [5 REST vs message queue](docs/questions/05-rest-vs-message-queue.md) | [14 Orchestration vs choreography](docs/questions/14-orchestration-vs-choreography.md) | [23 Logging and monitoring](docs/questions/23-logging-and-monitoring.md) |
| [6 Advantages and challenges](docs/questions/06-advantages-and-challenges.md) | [15 CQRS](docs/questions/15-cqrs.md) | [24 Monolith migration](docs/questions/24-monolith-migration.md) |
| [7 Service boundaries](docs/questions/07-service-boundaries.md) | [16 Eventual consistency](docs/questions/16-eventual-consistency.md) | [25 Challenges faced](docs/questions/25-challenges-faced.md) |
| [8 Domain-Driven Design](docs/questions/08-ddd.md) | [17 Fault tolerance](docs/questions/17-fault-tolerance.md) | |
| [9 Bounded context](docs/questions/09-bounded-context.md) | [18 Retry and circuit breaker](docs/questions/18-retry-and-circuit-breaker.md) | |

---

## How it's built

- **.NET 8 LTS**, minimal APIs, EF Core, SQLite per service (PostgreSQL in compose)
- **MassTransit over RabbitMQ**, held at v8 — v9 went commercial
- **Vertical slices**: each feature owns its endpoint, handler, validation and persistence in
  one folder, which is also what makes it an extraction seam later
- **Test-first**, with the failure cases written first — the saga tests written failure-first
  are the ones that found nothing later, because the bugs were never written
- **SOLID where it pays**, and documented where it was skipped: no generic repository over EF
  Core, no interface-per-class, no MediatR

The git history is the intended reading order — each phase is one commit that leaves the system
runnable.

## Seven bugs worth knowing about

Every one was found by **running** the system, not by a unit test. They are written up in
[question 25](docs/questions/25-challenges-faced.md), and they are better interview material
than any pattern description:

1. A saga participant that stayed **silent** when it had nothing to undo — hanging the
   orchestrator until its timeout.
2. A business refusal thrown as an **exception**, so it got retried and dead-lettered while the
   saga waited.
3. A projection maintaining **counters** that could not tell a new KPI reading from a recovery.
4. A correlation id **generated** at the gateway that vanished at the first hop.
5. SQLite refusing to `ORDER BY` a `DateTimeOffset` — LINQ that compiles and works on SQL Server.
6. A truncation that threw only for service names two characters shorter than their neighbours.
7. The read-model **rebuild** endpoint calling five secured services without forwarding the
   caller's token — the same mistake as (4), in a second place, found only by running it.
