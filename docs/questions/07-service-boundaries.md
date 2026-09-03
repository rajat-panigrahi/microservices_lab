# 7. How do you identify or define microservice boundaries?

*Asked by: Virtusa, EPAM, Deloitte*

## The 60-second answer

> I look for **things that change for different reasons, at different rates, owned by
> different people** — which is Conway's law and the single responsibility principle pointing
> at the same place.
>
> The technique is Domain-Driven Design: find the bounded contexts, and make each one a
> service. The practical test I apply is: *if I change this, what else must I redeploy?* If the
> answer is "another service", the boundary is wrong.
>
> The clearest example from StrategyOps is Risk and Issues. They look almost identical — a
> title, an owner, a status, a severity — and my first instinct was one service. They are
> separate because they change for different reasons: a risk register is reviewed monthly by a
> risk owner, an issue is chased daily against an SLA. Different cadence, different people,
> different reasons to change. **The similarity of the tables is a trap.**

## Signals for a boundary

**Good signals**
- different rate and reason for change
- different people own it
- different scaling or availability needs
- a genuine business capability with its own vocabulary
- the language shifts — "project" means something different to Finance than to Delivery

**Bad signals (these produce distributed monoliths)**
- technical layers: an "API service" and a "database service"
- entities: a service per table
- team convenience today
- "it feels big"

## The tests I actually apply

1. **The redeploy test.** Change this; what else must ship? Anything else means the boundary
   is wrong.
2. **The chattiness test.** If two services talk constantly on every request, they are one
   service.
3. **The transaction test.** If two things must be atomic and cannot tolerate a saga, keep them
   together. A saga is a real cost, and sometimes the right answer is not to pay it.
4. **The vocabulary test.** If the same word means different things on each side, that is a
   context boundary — see Q9.

## In this repo

| Boundary | Why |
|---|---|
| Projects | owns the project lifecycle and the stage machine |
| KPI | measurement, a different cadence and a different audience |
| Risk / Issues | separate: *might happen* vs *has happened*, different owners and rhythms |
| Benefits | finance's view of value; different stakeholders entirely |
| Reporting | not a business capability — a read model, and the one boundary drawn for a *technical* reason, which I would call out rather than pretend otherwise |

## The link to vertical slices

Inside each service, features are organised as vertical slices — a folder holding the
endpoint, command, handler and validation for one use case. That is the same idea one level
down, and it pays off exactly here: **a slice is an extraction seam.** When a service grows too
big, the question "which slices move?" has an answer, because a slice already owns its whole
path from HTTP to database. A layered codebase gives you no seam — the use case is smeared
across four projects and every extraction is a refactor first.

## Follow-up probes

**"What if you get a boundary wrong?"**
> Merging two services is much easier than splitting one, so I start coarse. If two services
> are constantly chatty or always deploy together, I merge them and stop pretending.

**"How do you handle an entity that spans boundaries?"**
> It usually is not one entity. A "project" in Risk is just an id and a code — the risk
> register does not need the budget or the sponsor. Each context keeps the slice of the concept
> it needs. Trying to share one canonical `Project` model across services is how you end up
> with a shared library that everyone must upgrade in lockstep.

**"Where do you start on a legacy system?"**
> Event storming with the domain experts, then extract the boundary with the fewest inbound
> dependencies first — for the practice, not the value. See Q24.
