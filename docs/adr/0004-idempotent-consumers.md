# ADR 0004 — Every consumer is idempotent, by construction

**Status:** Accepted · **Date:** 2026-08-31

## Context

The outbox (ADR 0003) gives **at-least-once** delivery. That is not a weakness to be
engineered away — it is the only guarantee a network can honestly offer. Exactly-once
*delivery* does not exist; exactly-once *processing* does, and it is the consumer's
job, not the broker's.

Messages here get delivered twice for at least four ordinary reasons:

1. The outbox publisher crashes after the broker accepted a message but before the
   row was marked processed.
2. RabbitMQ redelivers anything that was not acked.
3. MassTransit's retry policy replays on a transient failure.
4. A human retries a request that already partly succeeded.

## Decision

Consumers derive from `IdempotentConsumer<TDbContext, TMessage>`, which:

1. reads `context.MessageId` — the id assigned once at enqueue time and carried onto
   the wire deliberately (`MassTransitIntegrationEventPublisher` sets it, because
   MassTransit would otherwise mint a fresh one per publish and defeat the whole
   mechanism);
2. **stages** a claim on `(MessageId, ConsumerName)` in the `inbox_messages` table;
3. runs the consumer's work, which stages its own changes;
4. calls `SaveChanges` **once**.

The ordering in steps 2–4 is the entire point. Committing the claim separately from
the work opens a window where the service has recorded "handled" but crashed before
doing it — which loses the message permanently, the exact failure idempotency was
meant to prevent.

The key is `(MessageId, Consumer)`, not `MessageId` alone. One event often has several
consumers inside one service; keying on the id alone would let whichever ran first
suppress the others.

## Two layers, because one is not enough

The inbox handles **redeliveries of the same message**. It does nothing about a
*different* message describing the same fact — a second `RiskEscalated` for a risk
that already escalated, with a fresh id. So the domain guards that too:

| Layer | Catches | Example in this repo |
|---|---|---|
| Inbox (`inbox_messages`) | the same message id, again | `TheSameEscalationDeliveredTwice_RaisesOnlyOneIssue` |
| Unique index | two rows that must not both exist | one register per project; one issue per `OriginRiskId` |
| Domain rule | a business duplicate with a new id | `Risk.Escalate` throws once materialised; `Project.SetHealth` reports whether anything changed |

`TwoDifferentEscalationsOfTheSameRisk_StillRaiseOnlyOneIssue` exists specifically to
show the second and third layers doing the work the first cannot.

## Consequences

- A consumer must never call `SaveChanges` itself. The base class enforces the
  transaction boundary; `ConsumeOnceAsync` only stages.
- A message that cannot be acted on (an issue for an unknown project) is logged and
  dropped rather than thrown — a message that keeps throwing becomes a poison message
  that blocks everything queued behind it.
- The `inbox_messages` table grows forever. In production it needs a retention job
  that deletes rows older than the broker's maximum redelivery window.
- Consumers report business failures as **events** (`RiskRegisterProvisionFailed`)
  rather than exceptions. An exception gets retried and eventually dead-lettered; a
  saga waiting on that leg would simply hang.
