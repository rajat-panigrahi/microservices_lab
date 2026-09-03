# 8. What is Domain-Driven Design, and how is it related to microservices?

*Asked by: EPAM, Deloitte, Publicis Sapient*

## The 60-second answer

> DDD is an approach where the code models the business domain in the business's own language,
> and where you accept that a large domain has **several models** rather than one canonical
> one.
>
> Its relationship to microservices is simple and important: **DDD gives you the technique for
> finding service boundaries.** A bounded context is a candidate microservice. Without DDD,
> people split by technical layer or by table, and get a distributed monolith.
>
> The pieces I actually used in StrategyOps are the tactical ones — aggregates that own their
> invariants, domain events, and the ubiquitous language — plus the strategic one that matters
> most, bounded contexts.

## The parts worth naming

**Strategic (this is the part that decides your architecture)**
- **Ubiquitous language** — the domain expert's words, in the code. A `RiskRegister` is
  `Provision`ed and a risk is `Escalate`d, because that is what the PMO says.
- **Bounded context** — a boundary within which a model is consistent. See Q9.
- **Context map** — how contexts relate: shared kernel, customer/supplier, anti-corruption layer.

**Tactical (how you write a service)**
- **Aggregate** — a consistency boundary with one root. `Project` is one; every stage
  transition goes through it, and every illegal one throws.
- **Value object** — defined by its value, not an id.
- **Domain event** — something that happened, in the domain's language.
- **Repository** — collection-like access to aggregates.

## In this repo

- Aggregates enforcing their own rules: [`Project`](../../src/Services/StrategyOps.Projects.Api/Domain/Project.cs), [`Risk`](../../src/Services/StrategyOps.Risk.Api/Domain/Risk.cs), [`BenefitProfile`](../../src/Services/StrategyOps.Benefits.Api/Domain/BenefitProfile.cs)
- Domain events promoted to integration events: [`StrategyOps.Contracts`](../../src/BuildingBlocks/StrategyOps.Contracts)
- Ubiquitous language throughout: `Escalate`, `Materialised`, `Realise`, `Provision` — not
  `UpdateStatus` and `SetFlag`

## Aggregates decide your transaction boundaries

This is the connection people miss. **One aggregate = one transaction.** If two things must
change atomically they belong in the same aggregate — and therefore the same service. If they
can be eventually consistent, they can be separate aggregates and separate services.

So `Project` and `Risk` are separate aggregates in separate services, and that is exactly why
initiating a project needs a saga. The aggregate boundaries *caused* the distributed
transaction. Getting them right is how you avoid needing sagas everywhere.

## What I deliberately did not do

No generic `IRepository<T>` over EF Core. `DbContext` is already a unit of work plus a
repository; wrapping it adds a layer that only forwards calls. The pattern's benefit is
swapping persistence, and a service that owns its own database rarely does. Being able to say
*why* you skipped a pattern is worth more than having applied it.

## Follow-up probes

**"Is DDD required for microservices?"**
> Not required, but without it people boundary by technical layer or by table. DDD is the best
> technique I know for finding boundaries that are stable, because business capabilities change
> more slowly than technical designs.

**"Anaemic domain model — is that bad?"**
> It is a smell, not a sin. If entities are just property bags and all logic sits in services,
> you have procedural code with extra ceremony. My aggregates have behaviour: `Risk.Escalate`
> throws if the risk already materialised, and that single rule is what stops a retried HTTP
> request starting the choreography chain twice.

**"How do you do DDD on a CRUD-heavy system?"**
> You mostly do not. If a context is genuinely CRUD, model it as CRUD. DDD earns its cost where
> the rules are complex. Applying it uniformly is how it gets a reputation for ceremony.
