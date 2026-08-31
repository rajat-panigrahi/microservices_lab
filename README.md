# StrategyOps — a .NET microservices lab you can actually run

A small but enterprise-shaped **strategic portfolio management** system, built as a hands-on
answer to the 25 microservices questions that come up in .NET interviews.

Strategic Objectives are delivered by **Projects**, measured by **KPIs**, threatened by
**Risks** — which materialise into **Issues** — and justified by **Benefits**.

That domain isn't decoration. It produces the three problems microservices interviews
are really about:

| Problem the domain creates | Pattern it forces you to learn |
|---|---|
| Initiating a project touches Projects, KPI, Risk and Benefits at once | Distributed transaction → **saga with compensation** |
| A risk materialises → an issue is raised → project health drops → a benefit is at risk | **Event choreography** across four services |
| One portfolio dashboard has to read across five separate databases | **CQRS** read model + **eventual consistency** |

## Status

🚧 **Under construction.** Being built in phases — see [`docs/00-plan.md`](docs/00-plan.md)
for the full roadmap, architecture and the question-to-code map.

| Phase | Contents | Status |
|---|---|---|
| 0 | Plan + repo skeleton | ✅ |
| 1 | Contracts, BuildingBlocks, Projects.Api | ⏳ |
| 2 | Risk.Api, Issues.Api, RabbitMQ, choreography | ⏳ |
| 3 | Kpi.Api, Benefits.Api, ProjectInitiationSaga | ⏳ |
| 4 | Reporting.Api CQRS read model + live dashboard | ⏳ |
| 5 | Identity (JWT), Discovery, YARP Gateway | ⏳ |
| 6 | Observability, correlation, contract tests | ⏳ |
| 7 | Monolith sample, Docker/K8s, the 25 question docs | ⏳ |

## How it's built

- **.NET 8 LTS**, minimal APIs, EF Core, SQLite by default (database per service, zero setup)
- **RabbitMQ + MassTransit** for asynchronous integration events
- **Vertical slice** architecture — each feature owns its endpoint, handler, validation and persistence
- **Test-first** — domain tests, slice tests, messaging tests, contract tests
- **SOLID where it pays**, and documented judgement calls where it doesn't

## Reading order

Start at [`docs/00-plan.md`](docs/00-plan.md). The git history is the intended reading
order — each phase is one commit that leaves the system runnable.
