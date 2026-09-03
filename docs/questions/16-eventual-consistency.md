# 16. How do you maintain data consistency / eventual consistency across microservices?

*Asked by: Cognizant, Globant, EPAM, Deloitte*

## The 60-second answer

> You give up immediate consistency across services and engineer for eventual consistency
> instead. Three mechanisms do the work, and they solve three different problems:
>
> - a **transactional outbox**, so a state change and the message announcing it commit
>   atomically — otherwise you lose messages;
> - **idempotent consumers** with an inbox, so a redelivered message does not double-apply —
>   because delivery is at-least-once, always;
> - a **saga** with compensation, so a multi-service operation ends in a defined state.
>
> The one people miss is the first. A handler that saves to the database and then publishes has
> a window where the process can die in between: the project exists and nobody downstream will
> ever hear about it. Reversing the order is no better — then the event fires for a project that
> was never saved. **There is no ordering of two independent writes that is safe.**

## The dual-write problem, and the fix

```csharp
// Broken, in a way that looks fine and fails rarely enough to reach production:
db.Projects.Add(project);
await db.SaveChangesAsync();                       // committed
await bus.Publish(new ProjectDraftCreated(...));   // process dies here

// The outbox:
db.Projects.Add(project);
outbox.Enqueue(new ProjectDraftCreated { ... });
await db.SaveChangesAsync();   // ONE local transaction: the row AND the message
```

`outbox_messages` lives in the **service's own database**, so this is a single local
transaction — no distributed transaction, no two-phase commit. A background publisher then
drains it onto the bus.

Three details in the implementation that are easy to get wrong:

1. **Order by an auto-assigned sequence, not a timestamp.** Two events written in the same
   millisecond tie, and a clock that steps backwards reorders them — both reorder messages for
   the same aggregate.
2. **A failed publish stops the batch**, so a poison message cannot let later events overtake it.
3. **The message id is assigned once, at enqueue time**, and survives every redelivery — which
   is what makes consumer-side deduplication possible at all.

## Consistency you can actually promise

| Guarantee | Achievable across services? |
|---|---|
| Immediate consistency | ❌ not without distributed transactions |
| Eventual consistency | ✅ this |
| Read-your-own-writes | ⚠️ only by reading from the owning service, not the read model |
| Monotonic reads | ⚠️ needs sticky routing or version checks |

Being able to say what you *cannot* promise is worth more in an interview than claiming you
solved consistency.

## Making the window visible

The lag here is the outbox poll plus the broker hop — usually a second or two. Rather than
hiding it, the dashboard shows it: a SignalR push when a row changes and an "updated Ns ago"
column. Escalate a risk in one terminal and watch the row go red a second later.

Design so the window is safe to observe. The project sits in an explicit `Initiating` stage
rather than pretending to be `Active`, and the aggregate refuses most operations while it is
there.

## In this repo

- [Outbox](../../src/BuildingBlocks/StrategyOps.BuildingBlocks/Outbox) and [inbox](../../src/BuildingBlocks/StrategyOps.BuildingBlocks/Inbox)
- [ADR 0003 — transactional outbox](../adr/0003-transactional-outbox.md)
- [ADR 0004 — idempotent consumers](../adr/0004-idempotent-consumers.md)

## Follow-up probes

**"Why not just use a distributed transaction?"**
> Two-phase commit holds locks across the network, needs every participant to support it —
> RabbitMQ does not — and couples everyone's availability to everyone else's. The saga trades
> atomicity for availability, which is almost always the right trade in this setting.

**"How do you explain a stale number to a business user?"**
> By making it visible rather than arguing about it: show the last-updated time. "This is
> accurate as of two seconds ago" is a statement people accept; a number that is silently wrong
> is not.

**"What if a consumer is down for hours?"**
> Messages queue up and are processed on recovery — that is the point. What you must monitor is
> queue depth, because a queue quietly growing at 3am is much harder to notice than a 500. And
> if it exceeds the broker's retention, you have lost messages, which is when the rebuild
> endpoint stops being a nicety.
