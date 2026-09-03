# 6. Advantages and disadvantages / challenges of microservices

*Asked by: Infosys, Cognizant, Virtusa, Capgemini*

## The 60-second answer

> The advantages are mostly about **independence**: deploy one service without coordinating a
> release, scale the one part that is hot, contain a failure to one capability, and let teams
> own something end to end.
>
> The challenges are all versions of one thing: **you replaced method calls with a network.**
> The network fails, is slow, and reorders. So you lose transactions, you lose the stack trace,
> and you gain operational surface.
>
> The honest summary is that microservices trade **development-time simplicity for
> deployment-time flexibility**. If your pain is "we cannot release without three teams
> agreeing", that is a good trade. If your pain is "the code is messy", it is a terrible one —
> distribution does not clean up a mess, it just distributes it.

## Advantages, and what each really requires

| Advantage | Only real if… |
|---|---|
| Independent deployment | services do not share a database and can be released alone |
| Independent scaling | the bottleneck genuinely is one service |
| Fault isolation | you have breakers and fallbacks — otherwise failure just spreads more slowly |
| Technology freedom | you can afford to operate several stacks |
| Team autonomy | teams own services end to end, including on-call |

Every one has a precondition. A "microservices" system where all services share a database has
none of these advantages and all of the costs.

## Challenges, ranked by how much they actually hurt

1. **No distributed transactions.** The big one. See Q12–14 — sagas, compensation, and the
   fact that "rollback" becomes a business operation you have to design.
2. **Eventual consistency.** Your read model is stale by a second. The engineering is
   manageable; explaining it to a stakeholder who expects the number to be right is harder.
3. **Debugging across processes.** No stack trace spans services. Correlation ids and
   distributed tracing are not nice-to-haves, they are the replacement for the debugger.
4. **Operational surface.** Nine deployments, nine sets of logs, nine dashboards, nine on-call
   runbooks. This is why the platform investment has to come first.
5. **Data duplication and drift.** The read model holds copies. Copies drift. You need a way
   to rebuild them.
6. **Contract versioning.** Renaming a field breaks consumers you cannot see, at runtime.
7. **Latency.** Every hop is a network call. The aggregation endpoint takes ~480ms for what
   one SQL query does in ~5ms.
8. **Testing.** No single process to spin up. Hence four test tiers instead of two.

## What this repo cost, concretely

| Capability | Monolith | Microservices |
|---|---|---|
| Create a project + scorecard + register + benefit | ~20 lines, one transaction | saga + 6 contracts + 3 compensating consumers + tests |
| Risk escalation chain | 5 lines | 4 consumers across 3 services |
| Portfolio dashboard | 1 SQL query | read model, 17 projections, inbox, rebuild endpoint |
| Not losing a message | free | transactional outbox in every service |
| Not double-processing | free | inbox keyed on (MessageId, Consumer) |
| Knowing what happened | breakpoint | correlation id through HTTP and RabbitMQ |

## Follow-up probes

**"Which challenge surprised you most?"**
> How much of the work is not the happy path. The saga's forward path is short; nearly all its
> complexity is the three failure modes — a leg refusing, a leg never answering, and a leg
> answering *late*, after compensation started. That last one is the case people forget, and
> it silently orphans records for months.

**"How do you decide it is worth it?"**
> I look for a specific pain: releases blocked on other teams, or one component with genuinely
> different scaling needs. If I cannot name the pain, the answer is a modular monolith.
