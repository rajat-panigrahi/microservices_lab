# ADR 0003 — Transactional outbox for every state change

**Status:** Accepted · **Date:** 2026-08-31

## Context

A handler that changes state and then publishes an event does two writes to two
different systems:

```csharp
db.Projects.Add(project);
await db.SaveChangesAsync();        // committed
await bus.Publish(new ProjectDraftCreated(...));   // process dies here
```

If the process dies between them, the project exists and nobody downstream will
ever hear about it. Reversing the order is no better — then the event fires for a
project that was never saved. There is no ordering of two independent writes that
is safe, and this **dual-write problem** is the single most common source of
"our services drifted out of sync" in event-driven systems.

## Decision

Handlers never publish. They enqueue:

```csharp
db.Projects.Add(project);
outbox.Enqueue(new ProjectDraftCreated { ... });
await db.SaveChangesAsync();   // ONE transaction: the row and the message
```

`outbox_messages` lives in the **service's own database**, so this is a single
local transaction — no distributed transaction, no two-phase commit. A background
`OutboxPublisherService` then polls for unprocessed rows and hands them to
`IIntegrationEventPublisher`.

Three details that are easy to get wrong, and are pinned by tests here:

1. **Ordering is by an auto-assigned `Sequence`, not by timestamp.** Two events
   written in the same millisecond tie, and a clock that steps backwards reorders
   them — both of which can reorder messages for the same aggregate. The outbox
   table is therefore keyed by `Sequence` (an autoincrement integer) with the
   message id carried as a unique index.
2. **A failed publish stops the batch** rather than skipping ahead, so a poison
   message cannot let later events for the same aggregate overtake it.
3. **The message id is assigned once, at enqueue time**, and survives every
   redelivery — which is what makes consumer-side deduplication possible at all.

## Consequences

- Delivery is **at-least-once**, never exactly-once. The publisher can crash after
  the broker accepted a message but before the row was marked processed, and the
  message goes again. Every consumer must therefore be idempotent — that is what
  the inbox in phase 2 is for. "Exactly-once delivery" is not a thing you can buy;
  idempotent processing is what people actually mean when they claim it.
- The system is **eventually** consistent by a bounded lag — here, the poll
  interval. Phase 4's dashboard makes that lag visible on purpose.
- `IIntegrationEventPublisher` keeps the transport out of the outbox. Phase 1 runs
  a logging implementation so the service is useful before RabbitMQ exists; phase 2
  swaps in MassTransit without touching a handler, an aggregate, or a test.
- In production you would often pair polling with change-data-capture (Debezium)
  or use MassTransit's own outbox, so latency is not bounded by the poll interval.
