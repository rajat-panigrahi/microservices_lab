# 5. REST API vs message queue / event bus — when would you use each?

*Asked by: Virtusa, Cognizant, Globant*

## The 60-second answer

> I use REST when the caller needs an answer now, and messaging when I am telling the system
> something happened. The question I ask is: **"if the other service is down for five minutes,
> should this operation fail?"** If yes, call it. If no, publish.
>
> A concrete pair from StrategyOps: the portfolio dashboard reads over HTTP because a user is
> looking at a screen and needs data now. Risk escalation publishes to RabbitMQ because whether
> an issue gets raised one second or thirty seconds later does not change the business outcome
> — but *losing* it would.

## The decision table

| Question | REST | Messaging |
|---|---|---|
| Does the caller need the result to continue? | ✅ | ❌ |
| Should this fail if the other side is down? | ✅ | ❌ |
| Do several services care about this? | ❌ (N calls) | ✅ (one publish) |
| Might new consumers appear later? | ❌ (change the caller) | ✅ (change nothing) |
| Is the work slow or spiky? | ❌ | ✅ (queue absorbs it) |
| Do you need ordering and replay? | ❌ | ✅ |
| Is a human waiting? | ✅ | ❌ |

## The one that decides most arguments

**How many services care, and will that change?**

`RiskEscalated` has three consumers today. With REST, the Risk service would need three calls,
would know all three services by name, and adding a fourth reaction means changing and
redeploying Risk. With an event, Risk publishes one message and knows nothing — the fourth
consumer is a new class in a different repository.

That is the coupling difference, and it is worth more than the availability difference.

## In this repo

| Path | Style | Why |
|---|---|---|
| `GET /api/portfolio/{id}/overview` | REST fan-out | user waiting; needs an answer now |
| `POST /reporting/rebuild` | REST | deliberately wants a consistent snapshot *now* |
| `RiskEscalated` | event | three consumers, all independent |
| `ProvisionRiskRegister` | command over the bus | one handler, but the sender waits for a reply |
| Read model updates | events | must survive the consumer being down |

## Follow-up probes

**"Queue or topic?"**
> A queue is point-to-point: one message, one consumer — right for commands. A topic (or a
> RabbitMQ exchange) is publish/subscribe: every subscriber gets a copy — right for events.
> MassTransit gives each consumer its own queue bound to the exchange, so I get both.

**"Kafka or RabbitMQ?"**
> RabbitMQ is a broker: it routes a message, the consumer acks, it is gone. Kafka is a
> distributed log: messages persist, consumers track their own offset and can replay. Choose
> Kafka when you need replay, event sourcing, or very high throughput; RabbitMQ when you need
> routing, per-message acks and dead-lettering. This system needs routing and acks, so
> RabbitMQ.

**"What if a message keeps failing?"**
> Retry with backoff, then dead-letter it. MassTransit does this automatically — the `_error`
> queue is the first place I look when a consumer "isn't firing". What you must not do is
> retry forever: one poison message then blocks everything behind it.
