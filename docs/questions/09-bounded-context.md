# 9. What is a bounded context and how does it help define microservices?

*Asked by: EPAM, ThoughtWorks, Deloitte*

## The 60-second answer

> A bounded context is the boundary within which a model and its language mean one specific
> thing. Outside it, the same word means something else — and that is fine, because each
> context keeps its own model.
>
> It helps define services because **a bounded context is the natural unit of a microservice**:
> inside it the model is consistent and can be enforced with transactions; between contexts you
> translate, and consistency is eventual.
>
> The test I use is linguistic. "Project" in my Projects service means a stage machine with a
> sponsor and a budget. In the Risk service, "project" means an id and a code — nothing else,
> because a risk register does not care about the budget. Same word, two models, and trying to
> share one canonical `Project` class across both would couple two services that have no reason
> to change together.

## Why one canonical model fails

The instinct is a shared `Project` class in a common library. It fails predictably:

- it accumulates every field every context needs, and becomes a god object
- every service must upgrade in lockstep to change it — you have reinvented the monolith with
  extra steps
- it is wrong everywhere: full of nullable fields that are mandatory in one context and
  meaningless in another

Bounded contexts say: stop trying. Let each context have the model it needs.

## In this repo

| Context | What "project" means there |
|---|---|
| Projects | full aggregate: code, name, sponsor, budget, stage machine, health |
| KPI | an id and a code, to hang a scorecard on |
| Risk | an id and a code, to own a register |
| Issues | an id, to group issues under |
| Benefits | an id, a code and a **budget** — the only other context that needs it, to derive the forecast |
| Reporting | a denormalised row that copies fields from all five |

Nobody shares a class. Each receives what it needs in an event, keeps its own copy, and that
copy is *its* model rather than a reference to someone else's.

## The interesting pair: Risk vs Issues

Two contexts that look like one:

| | Risk | Issue |
|---|---|---|
| Means | might happen | has happened |
| Scored on | probability × impact | severity |
| Reviewed | monthly, by a risk owner | daily, against an SLA |
| Owned by | risk owner | delivery manager |
| Closed when | it expires or materialises | it is resolved |

Same-shaped tables, different language, different rhythm, different people. Splitting them is
the whole lesson: **similar data is not a shared context.**

## Context mapping patterns worth naming

- **Shared kernel** — a small shared model. Here: `StrategyOps.Contracts`, and only that,
  because every shared type is a coordination cost.
- **Customer/supplier** — downstream depends on upstream's contract. Projects supplies
  `ProjectInitiationRequested`; KPI, Risk and Benefits consume it.
- **Anti-corruption layer** — translate a foreign model at the edge rather than letting it
  leak. The rebuild endpoint's local `Upstream*` records are exactly this: it deliberately does
  not reference the other services' DTOs, because sharing DTOs between producer and consumer is
  how two services quietly become one deployable.
- **Published language** — a versioned contract everyone agrees on: `Contracts/V1`.

## Follow-up probes

**"Is a bounded context always one microservice?"**
> One context should never be split across services — that guarantees a distributed monolith.
> But one service *can* hold several small contexts, and starting that way is safer. The rule
> is one-way: never split a context, sometimes combine them.

**"How do you find them?"**
> Event storming with domain experts, listening for where the language changes. When someone
> says "well, when Finance says benefit they mean something different", that is a boundary
> being handed to you.
