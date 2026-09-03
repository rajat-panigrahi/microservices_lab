# 1. What are microservices? Explain microservices architecture.

*Asked by: TCS, Infosys, Cognizant, Capgemini, Wipro, Virtusa, LTI, Accenture, Globant*

## The 60-second answer

> Microservices is an architectural style where one application is built as a set of small,
> independently deployable services, each owning one business capability and its own data,
> communicating over the network.
>
> The word that matters is **independently deployable**. If I have to release two services
> together, they are one service in two repositories — I have paid every cost of distribution
> and bought none of the benefit.
>
> In the system I built, StrategyOps, there are six business services — Projects, KPI, Risk,
> Issues and Benefits, plus a Reporting read model — behind an API gateway. Each owns its own
> database. The Risk service cannot read the Projects table; it finds out a project exists
> because Projects publishes an event. That constraint is the architecture. Everything else —
> the saga, the outbox, the read model — exists to cope with it.

## The three properties that actually define it

| Property | What it means | Where it shows in this repo |
|---|---|---|
| **Independently deployable** | ship one service without coordinating a release | nine separate projects, nine images, one shared Dockerfile |
| **Owns its data** | no other service touches its tables | six databases; `deploy/docker/init-databases.sql` creates them separately |
| **Aligned to a business capability** | organised around what the business does, not around technical layers | Risk vs Issues, not "Controllers service" and "Repositories service" |

Everything else people list — small, containerised, REST, DevOps culture — is common but not
definitional. Plenty of systems tick those boxes and are still a distributed monolith.

## In this repo

- Six services under [`src/Services/`](../../src/Services), each with its own `DbContext`
- The event contracts that are the only shared code: [`StrategyOps.Contracts`](../../src/BuildingBlocks/StrategyOps.Contracts)
- The same domain as a single application, for comparison: [`src/Monolith/`](../../src/Monolith)

## Follow-up probes

**"How small is a microservice?"**
> There is no line count. The test I use is: can one team own it, can it be rewritten in a few
> weeks, and does it have one reason to change? My Risk service is about 800 lines. Size is a
> symptom of a good boundary, not the goal — "micro" is the most misleading word in the name.

**"Is it always the right choice?"**
> No, and I would default to a monolith. Microservices buy independent deployment, independent
> scaling and team autonomy. You pay in network failure, eventual consistency, distributed
> debugging and operational overhead. Below roughly five teams that trade is usually bad —
> you get all the cost and none of the benefit, because one team can just deploy the monolith.

**"What is a distributed monolith?"**
> Services that have to be deployed together — usually because they share a database, or
> because a synchronous call chain means service A is down whenever D is. It is the worst of
> both worlds. The two things I do to avoid it are database-per-service and events instead of
> synchronous calls for anything that is not a query.
