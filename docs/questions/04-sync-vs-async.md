# 4. Synchronous vs asynchronous communication in microservices

*Asked by: Cognizant, EPAM, Deloitte, Virtusa*

## The 60-second answer

> Synchronous means the caller blocks and gets an answer; asynchronous means it hands the
> message off and moves on. The real difference is not speed, it is **temporal coupling**:
> synchronous calls require both services to be up at the same instant, asynchronous ones do
> not.
>
> That is what drives the choice. In StrategyOps, escalating a risk publishes an event and
> returns in ~40ms. Three services react afterwards. If the Issues service is down for two
> minutes, the message waits in RabbitMQ and is handled on recovery — nobody notices. Had I
> made it a synchronous call, escalating a risk would simply fail, and a Risk-service outage
> and an Issues-service outage would be the same event to the user.
>
> The price is that I gave up "it is done when the call returns". Now it is done *eventually*,
> and I need idempotent consumers, an outbox, and a way to see where a message got to.

## Choosing between them

**Use synchronous when:**
- a human is waiting for the answer (a query, a screen)
- you need the result to continue
- the operation is genuinely read-only

**Use asynchronous when:**
- you are announcing a fact rather than requesting an answer
- the reaction can be slightly late without harm
- more than one service cares, or might later
- the work is slow, or the callee is less available than you need to be

## The costs, honestly

| | Synchronous | Asynchronous |
|---|---|---|
| Both up at once? | required | not required |
| Failure mode | request fails immediately | message waits; failure is invisible until you look |
| Consistency | immediate | eventual |
| Debugging | stack trace | correlation ids across processes |
| Backpressure | callers pile up, thread pools exhaust | queue grows — visible, and survivable |
| Extra machinery needed | retry, breaker, timeout | outbox, inbox, DLQ, monitoring |

Async is not "better". It moves the difficulty from *availability* to *observability*: a queue
quietly growing at 3am is harder to notice than a 500.

## In this repo

- Sync, with all three resilience policies: [`ResilienceExtensions`](../../src/BuildingBlocks/StrategyOps.BuildingBlocks/Resilience/ResilienceExtensions.cs)
- Async, with everything it requires: [outbox](../../src/BuildingBlocks/StrategyOps.BuildingBlocks/Outbox), [inbox](../../src/BuildingBlocks/StrategyOps.BuildingBlocks/Inbox)
- Measured: escalate returns in ~40ms; the project turns Red about a second later

## Follow-up probes

**"Async is fire-and-forget then?"**
> No — that is the dangerous version. Fire-and-forget means you never find out it failed. What
> I have is at-least-once delivery with retries, a dead-letter queue for messages that keep
> failing, and an outbox so the message cannot be lost between the database commit and the
> broker. "Async" without those is just losing data slowly.

**"How does the user know it worked?"**
> Return 202 with the resource, and let them see the state change when it arrives — which is
> what the SignalR dashboard does. The mistake is pretending it was synchronous and returning
> a success that has not happened yet.

**"Can you mix them?"**
> Constantly, and this system does. The initiation saga sends commands asynchronously but the
> user's POST returns synchronously with `Initiating` — an honest intermediate state rather
> than a fake completion.
