# 2. Monolithic vs microservices architecture

*Asked by: TCS, Infosys, Cognizant, Capgemini, Wipro, Virtusa, Globant, LTI, Persistent*

## The 60-second answer

> A monolith is one deployable with one database; microservices are many deployables each with
> their own. The difference people focus on is size, but the real difference is **where your
> boundaries are enforced**. In a monolith the compiler enforces them — and lets you cheat, so
> people do. Across services the network enforces them and cheating is impossible.
>
> I built the same domain both ways in this repo to see the price. The clearest single
> comparison: in the monolith, creating a project writes the project, its KPI scorecard, its
> risk register and its benefit profile in **one `SaveChanges`** — atomic, no compensation
> possible or needed. Across services that same operation is a saga: a state machine, six
> message contracts, three compensating handlers, timeout handling, and about 500 lines with
> tests.
>
> Same business outcome. That is the price, and it should be paid deliberately.

## Side by side, from this repo

| | Monolith ([`src/Monolith/`](../../src/Monolith)) | Microservices ([`src/Services/`](../../src/Services)) |
|---|---|---|
| Create a project with its scorecard, register and benefit | one `SaveChanges`, ~20 lines | `ProjectInitiationSaga` + 6 contracts + 3 compensating consumers |
| Escalate a risk → issue → project red → benefit at risk | 5 lines, one transaction | 4 consumers in 3 services, choreographed |
| Portfolio dashboard | one SQL query, always consistent | read model, 17 projections, an inbox, a rebuild endpoint |
| Deploy | one artifact | nine, independently |
| Scale the hot path | scale everything | scale Reporting only |
| A bug in Risk | takes down everything | degrades one section of one screen |
| Debugging | breakpoint | correlation ids across six logs |
| Onboarding a developer | read one solution | understand the boundaries first |

## What each is genuinely better at

**Monolith wins:** transactional consistency, refactoring across boundaries, local debugging,
simplicity of operations, latency (a method call is not a network call), and cost.

**Microservices win:** independent deployment (the big one), independent scaling, fault
isolation, technology choice per service, and team autonomy at scale.

Notice that most of the microservices wins are **organisational**. That is the honest framing:
this is primarily a solution to a people-and-deployment problem that happens to be expressed
in architecture.

## Follow-up probes

**"So when would you actually choose microservices?"**
> When one codebase has become a queue — several teams waiting on each other's releases. Or
> when one part has genuinely different scaling or availability needs. Not because it is
> modern, and not for a greenfield product where the domain is still moving; boundaries drawn
> before you understand the domain are the expensive kind of wrong.

**"What is a modular monolith?"**
> One deployable with enforced internal module boundaries — separate assemblies, no shared
> tables, communication through interfaces or in-process events. It gets you most of the
> discipline with none of the distribution cost, and it is the right first step, because those
> modules are what you would extract later. My vertical slices serve the same purpose one
> level down.

**"Can you go back?"**
> Yes, and teams do. Merging two services that turned out to be one bounded context is far
> easier than splitting one that was two. That asymmetry is a good argument for starting
> coarse.
