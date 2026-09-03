# 19. How do you handle duplicate messages / idempotency / retry failures?

*Asked by: Cognizant, EPAM, Deloitte*

## The 60-second answer

> You assume every message will arrive more than once, because it will. Message delivery is
> **at-least-once** — exactly-once delivery does not exist across a network. What does exist is
> exactly-once *processing*, and that is the consumer's job, not the broker's.
>
> The mechanism is an **inbox**: before doing the work, the consumer records that it has seen
> this message id. If the row already exists, it skips.
>
> The critical detail is the transaction boundary. The inbox row and the business change must
> commit **together**, in one transaction. Saving the claim separately opens a window where the
> service has recorded "handled" but crashed before doing the work — which loses the message
> permanently, the exact failure idempotency was meant to prevent.

## Why duplicates are normal, not exceptional

At least four ordinary reasons in this system alone:

1. The outbox publisher crashes after the broker accepted a message but before the row was
   marked processed.
2. RabbitMQ redelivers anything that was not acked.
3. MassTransit's retry policy replays on a transient failure.
4. A human retries a request that already partly succeeded.

## The implementation

```csharp
// IdempotentConsumer<TDbContext, TMessage>
var messageId = context.MessageId;                       // assigned once, at enqueue time

if (!await inbox.TryClaimAsync(messageId, consumerName)) // STAGES a row, does not save
{
    return;                                              // already handled; skip
}

await ConsumeOnceAsync(context);                         // stages the business change

await Db.SaveChangesAsync();                             // ONE transaction: claim + work
```

Three details worth knowing:

- **The key is `(MessageId, Consumer)`, not the id alone.** One event often has several
  consumers inside one service; keying on the id alone would let whichever ran first suppress
  the others.
- **The message id is assigned once**, when the event is written to the outbox, and is carried
  onto the wire deliberately. MassTransit would otherwise mint a fresh id per publish attempt,
  and a redelivery would look like a brand new message — defeating the whole mechanism.
- **The uniqueness is enforced by the database**, so two instances of the service racing on the
  same redelivered message cannot both win.

## One layer is not enough

The inbox catches **the same message id, again**. It does nothing about a *different* message
describing the same fact. So there are three layers:

| Layer | Catches | Example here |
|---|---|---|
| Inbox | the same message id, again | redelivered `RiskEscalated` → one issue |
| Unique index | two rows that must not both exist | one register per project; one issue per `OriginRiskId` |
| Domain rule | a business duplicate with a fresh id | `Risk.Escalate` throws once materialised; `Project.SetHealth` reports whether anything changed |

`TwoDifferentEscalationsOfTheSameRisk_StillRaiseOnlyOneIssue` exists specifically to show the
second and third layers doing what the first cannot.

## Designing events so duplicates are harmless

The cheapest idempotency is a contract that cannot double-count. `BenefitRealised` carries
`RealisedToDate` — the running total — as well as the increment, so the projection **sets**
rather than **adds**. A redelivery writes the same number. One small contract decision removes
a whole class of bug.

## In this repo

- [`IdempotentConsumer`](../../src/BuildingBlocks/StrategyOps.BuildingBlocks/Messaging/IdempotentConsumer.cs), [`InboxStore`](../../src/BuildingBlocks/StrategyOps.BuildingBlocks/Inbox/InboxStore.cs)
- [ADR 0004](../adr/0004-idempotent-consumers.md)

## Follow-up probes

**"Why not just use exactly-once delivery?"**
> It does not exist. To deliver exactly once you would need an atomic commit between the broker
> and the consumer's database — a distributed transaction, with all the problems that brings.
> Kafka's "exactly-once semantics" is exactly-once *processing* within Kafka, achieved with
> idempotent producers and transactional offsets. Outside Kafka you are back to at-least-once.

**"What about HTTP requests?"**
> An idempotency key: the client sends a unique header, the server stores the result against
> it and replays the same response for a repeat. This is what Stripe does for payments.

**"Doesn't the inbox table grow forever?"**
> Yes, and it needs a retention job deleting rows older than the broker's maximum redelivery
> window. Forgetting that is a slow-motion outage.

**"What if a message keeps failing?"**
> Retry with backoff, then dead-letter it — MassTransit's `_error` queue. What you must not do
> is retry forever: one poison message then blocks everything queued behind it. A message that
> cannot be acted on at all — an issue for a project this service has never heard of — is
> logged and dropped rather than thrown, for the same reason.
