# StrategyOps — a runnable .NET microservices lab for the Top-25 interview questions

## Context

You have solid C# but no microservices experience, and you have a list of 25 questions that TCS/Infosys/Cognizant/EPAM/Deloitte-class interviewers actually ask. Reading theory won't survive a follow-up probe; what survives is *"here is a system I built, here is the file where that pattern lives, here is what bit us."*

So this repo becomes **StrategyOps**: a small but genuinely enterprise-shaped strategic portfolio management system — Strategic Objectives delivered by **Projects**, measured by **KPIs**, threatened by **Risks**, which materialise into **Issues**, and justified by **Benefits**. That domain is not decoration: it produces a real distributed transaction (project initiation touches four services), a real event chain (risk → issue → project health → benefit at risk), and a real read-model problem (one portfolio dashboard over five databases). Every one of the 25 questions gets answered by code you can run, not prose.

Built **test-first**, in **vertical slices**, with **SOLID applied where it pays and skipped where it's ceremony** — because "how do you test microservices?" and "how did you apply SOLID?" are the two follow-ups that come right after the 25.

The repo is currently empty (README, LICENSE, .NET `.gitignore`), so this is greenfield.

### Everything lands in the repo

Nothing useful stays outside `rajat-panigrahi/microservices_lab`. This container is ephemeral, so the repo is the only durable artifact:

- **This plan** is committed first, as `docs/00-plan.md`, before any code — so the roadmap travels with the code and you can read it on GitHub.
- **All 25 question docs, the architecture write-up, ADRs, testing guide, demo script and diagrams** live in `docs/` and are committed with the phase they belong to.
- **A root `README.md`** rewritten as the front door: what the system is, the architecture diagram, how to run it, the learning path, and an honest list of what was and wasn't verified.
- Every phase is committed and **pushed** to `claude/microservices-interview-prep-h7l5en` as it completes — not batched at the end — so progress is visible on GitHub throughout.

### Environment constraints found during research

| Finding | Consequence for the plan |
|---|---|
| No .NET SDK; official SDK CDN hosts (`builds.dotnet.microsoft.com`, `dotnetcli.azureedge.net`) return **403 from the egress proxy** | Target **.NET 8 LTS**, installed via `apt-get install dotnet-sdk-8.0` (Ubuntu noble-updates has 8.0.125). Not .NET 9. |
| NuGet reachable (`api.nuget.org` 200) | MassTransit, YARP, Polly, EF Core, Serilog, OpenTelemetry, xUnit, FluentAssertions all restore fine. |
| **No Docker daemon** (CLI present, socket missing) | Dockerfiles/compose/k8s get written but **cannot be run or verified here**. Rules out Testcontainers for the test suite. |
| **RabbitMQ 3.12 and PostgreSQL 16 are apt-installable** | The full system *can* be run and verified end-to-end in this session, including real cross-service messaging. |

---

## How the code is organised

### Vertical slices, not layers

Each service is one project whose primary axis is **feature, not technical layer**:

```
StrategyOps.Risk.Api/
  Features/
    RaiseRisk/          Endpoint.cs  Command.cs  Handler.cs  Validator.cs
    RescoreRisk/        Endpoint.cs  Command.cs  Handler.cs  Validator.cs
    EscalateRisk/       ...
    GetRiskRegister/    Endpoint.cs  Query.cs    Handler.cs
    Consumers/          ProvisionRiskRegisterConsumer.cs  ProjectClosedConsumer.cs
  Domain/               Risk.cs  RiskRegister.cs  RiskTier.cs   ← shared across slices
  Infrastructure/       RiskDbContext.cs  Outbox wiring  Migrations/
  Program.cs            maps every Features/**/Endpoint.cs
```

A slice owns its request, validation, handler, response, and persistence access end to end. Adding "close a risk" means adding one folder, touching nothing else — which is exactly the property that makes a slice safe to later *extract into its own service*, so vertical slicing and microservice boundaries reinforce each other. The docs make that link explicit for Q7.

Only `Domain/` is shared inside a service (the aggregate is genuinely common), and only `StrategyOps.Contracts` is shared *between* services.

**No MediatR.** It went commercial, and interviewers now ask about that. Handlers are plain classes registered in DI and invoked directly from minimal-API endpoints — fewer moving parts, and it shows you understand that MediatR is a convenience, not the pattern. One ADR records the decision so you can answer the question either way.

### TDD as the build rhythm

Every slice is written **red → green → refactor**, and the commits show it:

1. **Domain test first** — `RiskTier_IsCritical_WhenProbabilityTimesImpactExceeds15()`. Pure, fast, no infrastructure. The risk scoring matrix, KPI RAG thresholds, benefit realisation %, and every saga state transition are driven out this way.
2. **Slice test second** — `WebApplicationFactory` + SQLite in-memory, hitting the real HTTP endpoint. Asserts status code, response shape, persisted state, **and that the right outbox row was written**.
3. **Messaging test third** — MassTransit `InMemoryTestHarness`: consumer published what it should, saga reached the expected state, compensation fired on failure. Runs with zero infrastructure.
4. **Contract test** — snapshot the shape of every event in `StrategyOps.Contracts`; the test fails if a field is renamed or removed, which is how you talk about consumer-driven contracts and event versioning when probed.

Test projects mirror the slice layout, so `Features/RaiseRisk/` has an obvious home in `tests/`. Target is meaningful coverage of domain + slice + messaging, not a coverage percentage.

### SOLID, applied where it pays

| Principle | Where it earns its place here |
|---|---|
| **S** | A slice handler does one use case. `OutboxPublisher` publishes; it does not decide what to publish. |
| **O** | New event consumers plug in without editing existing ones — the choreography chain extends by adding a consumer, never by editing a switch. |
| **L** | `IEventBus` implementations (RabbitMQ, in-memory harness) are substitutable — that's what makes the tests honest. |
| **I** | Small role interfaces: `IOutboxWriter`, `IInboxStore`, `IServiceRegistryClient` — not one fat `IRepository<T>` per service. |
| **D** | Handlers depend on `DbContext` + abstractions, so slice tests swap the provider and the transport with no production code change. |

Deliberately **not** applied: no interface-per-class ceremony, no generic repository over EF Core (EF is already a unit of work — and "why no repository pattern?" is a great answer to have ready), no abstraction introduced before a second implementation exists. Docs call these out as judgement calls, because reciting SOLID is worth less than explaining where you chose against it.

---

## Architecture

Nine services + two building-block libraries. SQLite file per service by default (database-per-service, zero setup); PostgreSQL via connection-string switch in compose. RabbitMQ + MassTransit for events.

```
Gateway (YARP, :5100) ──┬─ Projects  :5101   Identity  :5107
   JWT at the edge      ├─ Kpi       :5102   Discovery :5108
   Polly + aggregation  ├─ Risk      :5103
                        ├─ Issues    :5104
                        ├─ Benefits  :5105
                        └─ Reporting :5106  (CQRS read model + SignalR dashboard)
                                 all ↕ RabbitMQ
```

### Repo layout

```
StrategyOps.sln
src/
  BuildingBlocks/
    StrategyOps.Contracts/          integration events, versioned under V1/
    StrategyOps.BuildingBlocks/     outbox, inbox, correlation, resilience, auth,
                                    observability, discovery client, Result/ProblemDetails
  Services/                         9 services, each in Features/Domain/Infrastructure shape
  Gateway/StrategyOps.Gateway/
  Monolith/StrategyOps.Monolith/    the "before" picture for Q2 / Q24
tests/
  StrategyOps.Domain.Tests/         fast, pure, no infrastructure
  StrategyOps.Slice.Tests/          WebApplicationFactory per service, SQLite in-memory
  StrategyOps.Messaging.Tests/      MassTransit harness: consumers, saga, compensation
  StrategyOps.Contract.Tests/       event schema snapshots
deploy/  docker/  k8s/  local/run-all.sh
docs/    00-plan.md          this plan, committed first
         00-start-here.md    10-day learning path
         architecture.md     context map + mermaid diagrams
         testing.md          TDD rhythm, pyramid, contract tests
         demo-script.md      copy-pasteable curl walkthrough
         adr/                0001..0006 decision records
         questions/          01-*.md .. 25-*.md
README.md                    front door: what, how to run, what's verified
```

### Domain model (the part that makes the patterns real)

| Service | Aggregates | Key events published |
|---|---|---|
| Projects | `StrategicObjective`, `Project` (Draft→Initiating→Active→OnHold→Closed; Health G/A/R) | `ProjectInitiationRequested`, `ProjectActivated`, `ProjectInitiationFailed`, `ProjectHealthChanged`, `ProjectClosed` |
| Kpi | `KpiDefinition` (target, amber/red thresholds, direction), `KpiMeasurement` | `KpiScorecardProvisioned`, `KpiMeasurementRecorded`, `KpiBreached`, `KpiRecovered` |
| Risk | `RiskRegister`, `Risk` (Probability×Impact ⇒ Score ⇒ Tier) | `RiskRegisterProvisioned`, `RiskRaised`, `RiskRescored`, `RiskEscalated` |
| Issues | `Issue` (may carry `OriginRiskId`) | `IssueRaised`, `IssueAssigned`, `IssueResolved`, `IssueBreachedSla` |
| Benefits | `BenefitProfile` (forecast), `BenefitRealisation` (actual) | `BenefitProfileRegistered`, `BenefitRealised`, `BenefitAtRisk` |
| Reporting | `PortfolioScorecard` (flat read model) | — subscribes to all of the above |

**Two distributed flows, deliberately built differently** so Q13/Q14 answer themselves:

1. **Project Initiation Saga — orchestration.** `SubmitForInitiation` → MassTransit state machine in Projects sends `ProvisionScorecard` (Kpi), `ProvisionRiskRegister` (Risk), `RegisterBenefitProfile` (Benefits). All three succeed → `ProjectActivated`. Any one fails or times out → compensations (`DeleteScorecard`, `DeleteRegister`, `WithdrawBenefitProfile`) → `ProjectInitiationFailed`.
2. **Risk escalation — choreography.** `RiskEscalated` → Issues auto-creates an Issue → `IssueRaised` → Projects sets health Amber/Red **and** Benefits flags `BenefitAtRisk`. No coordinator. Docs contrast the two with the debugging cost of each.

Both flows write through a **transactional outbox** (entity + outbox row in one SQLite transaction, background publisher drains it) and are consumed through an **inbox** (`ProcessedMessages` keyed by `MessageId`, applied as a MassTransit filter) — so Q16 and Q19 are demonstrated, not asserted. Both have failing-test-first coverage in `StrategyOps.Messaging.Tests`.

---

## Build phases (one commit each — the git history is the reading order)

Every phase is tests-first; a phase is done when its tests are green, the service runs, its ADR is written, and the commit is **pushed**.

| # | Deliverable | Teaches |
|---|---|---|
| 0 | `docs/00-plan.md` (this plan) + README skeleton committed and pushed | — |
| 1 | Solution, `Contracts`, `BuildingBlocks` (outbox, Result, ProblemDetails, health), `Projects.Api` — first vertical slice driven out red-green-refactor, EF Core + SQLite + Swagger | Q1, Q2, Q7 |
| 2 | `Risk.Api`, `Issues.Api`, RabbitMQ + MassTransit, inbox idempotency, risk→issue **choreography** | Q3, Q4, Q5, Q19 |
| 3 | `Kpi.Api`, `Benefits.Api`, **ProjectInitiationSaga** with compensation + timeout, saga tests on the harness | Q12, Q13, Q14 |
| 4 | `Reporting.Api` CQRS read model, `/reporting/rebuild`, SignalR live dashboard | Q15, Q16 |
| 5 | `Identity.Api` (JWT), `Discovery.Api` (registry + client handler), `Gateway` (YARP, aggregation, Polly retry/breaker/timeout, rate limit), chaos endpoints | Q10, Q11, Q17, Q18, Q20 |
| 6 | Serilog + correlation ID through HTTP *and* message headers, OpenTelemetry traces/metrics, health probes, contract tests, `docs/testing.md` (pyramid, why no Testcontainers here) | Q23 |
| 7 | `Monolith` sample, Dockerfiles + compose + k8s manifests, the 25 question docs, final README | Q21, Q22, Q24, Q25 |

Phases 1–4 are each independently runnable, so you're never staring at a half-system. ADRs and the architecture doc are written in the phase that earns them, so the docs are never a big-bang at the end.

---

## Question → code map

The 25 docs each carry a 60-second spoken answer, a whiteboard sketch, follow-up probes with answers, common traps, and the links below.

| Q | Lives in |
|---|---|
| 1, 2 | `docs/architecture.md`, `src/Monolith/` vs `src/Services/` side by side |
| 3, 4, 5 | Gateway typed `HttpClient` (sync reads) vs MassTransit publish (async state change); decision table in `docs/questions/05-*.md` |
| 6, 25 | `docs/questions/06-*.md`, `25-*.md` — tied to specific commits where the pain showed up |
| 7, 8, 9 | `docs/adr/0002-service-boundaries.md`, context map, why Risk and Issues are separate contexts, and how a vertical slice becomes an extraction seam |
| 10 | `src/Gateway/` — routes, edge JWT, `/api/portfolio/{id}/overview` fan-out |
| 11 | `Discovery.Api/`, `BuildingBlocks/Discovery/DiscoveryHttpMessageHandler.cs`; docs map it to Consul / Eureka / K8s DNS |
| 12, 13, 14 | `Projects.Api/Features/Sagas/ProjectInitiationSaga.cs` + compensation consumers; choreography chain in Risk/Issues/Benefits |
| 15 | `Reporting.Api/` projections + rebuild endpoint |
| 16 | `BuildingBlocks/Outbox/`, visible lag on the SignalR dashboard |
| 17, 18 | `BuildingBlocks/Resilience/ResiliencePipelines.cs` (Polly v8), chaos endpoints that open the breaker on demand |
| 19 | `BuildingBlocks/Inbox/IdempotentConsumerFilter.cs`, idempotency-key filter on POSTs |
| 20 | `Identity.Api/`, `BuildingBlocks/Auth/`, role policies; docs cover real OAuth2/OIDC flows and why you'd use Entra ID/Duende instead of this |
| 21, 22 | `deploy/docker/`, `deploy/docker-compose.yml`, `deploy/k8s/` |
| 23 | `BuildingBlocks/Observability/`, `BuildingBlocks/Correlation/` |
| 24 | `src/Monolith/` + `docs/questions/24-*.md` strangler-fig walkthrough naming which seam is cut first and why |
| bonus | `docs/testing.md` (TDD, pyramid, contract tests), `docs/adr/0006-no-mediatr.md`, SOLID call-outs |

---

## Verification

Runnable and checkable **in this session**:

1. `apt-get install -y dotnet-sdk-8.0 rabbitmq-server` → `dotnet build` clean, `dotnet test` green across all four test projects.
2. Start RabbitMQ, launch all nine services via `deploy/local/run-all.sh`.
3. Walk `docs/demo-script.md` (curl, copy-pasteable):
   - token from Identity → create objective → create project draft → **submit for initiation**
   - assert saga completed: scorecard, risk register and benefit profile all exist; project is `Active`
   - raise a Critical risk → escalate → assert Issue auto-created, project health went Amber, benefit flagged at risk
   - `GET /reporting/scorecards` reflects all of it; dashboard updates live
4. **Compensation run**: force Benefits to reject → assert project lands in `InitiationFailed` *and* the KPI scorecard + risk register were rolled back.
5. **Idempotency**: replay the same `MessageId` twice → exactly one effect, one inbox row.
6. **Circuit breaker**: `POST /chaos/fail` on Kpi, hit the gateway aggregation repeatedly → breaker opens, fallback returned, health endpoint shows degraded.
7. **Correlation**: one `X-Correlation-Id` traced through gateway → service → RabbitMQ → consumer in the logs.

**Cannot be verified here — will be labelled as such in the README:** Docker image builds, `docker compose up`, and the Kubernetes manifests (no Docker daemon in this environment). They'll be written carefully and reviewed by eye, but expect to run them first on your own machine.

All work — code *and* every document, this plan included — goes on `claude/microservices-interview-prep-h7l5en`, committed per phase and pushed with `git push -u origin` as each phase completes. No PR unless you ask.
