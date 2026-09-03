# 12. How do you handle distributed transactions in microservices?

*Asked by: Deloitte, TCS, Virtusa, Cognizant, EPAM*

## The 60-second answer

> You do not — not in the ACID sense. Once each service owns its own database there is no
> transaction that spans them, and the classic answer, two-phase commit, is not a trade worth
> making: it holds locks across the network, and it makes every participant's availability
> everyone's availability.
>
> Instead you use a **saga**: a sequence of local transactions, each committing independently,
> each with a **compensating action**. If a later step fails, you run the compensations for the
> steps that already succeeded.
>
> The mental shift is that rollback stops being something the database does and becomes
> something **the business defines**. There is no undo — the work genuinely happened and is
> genuinely being reversed, visibly.
>
> In StrategyOps, initiating a project provisions a KPI scorecard, a risk register and a
> benefit profile across three services. If Benefits refuses the forecast — which it does when
> it exceeds the portfolio ceiling — the saga withdraws the scorecard and the register, and the
> project lands in `InitiationFailed` with the reason recorded.

## Why not two-phase commit

- It holds locks for the duration of a network round trip, so throughput collapses under
  contention.
- The coordinator is a single point of failure, and a crash at the wrong moment leaves
  participants blocked holding locks.
- It requires every participant to support it. RabbitMQ does not. Most cloud databases do not.
- It couples availability: if any participant is down, nothing commits.

## What you actually get instead

**Eventual consistency.** For a window — usually a second or two here — the system is
inconsistent: the project is `Initiating`, the scorecard exists, the benefit profile does not
yet. That window is not a bug, it is the design, and the job is to make it short, visible, and
safe to observe.

The three techniques that make it safe:

| Problem | Technique | In this repo |
|---|---|---|
| The state change committed but the message was lost | **transactional outbox** | entity + outbox row in one `SaveChanges` |
| The message arrived twice | **idempotent consumers** | inbox keyed on (MessageId, Consumer) |
| A step failed after others succeeded | **compensation** | `WithdrawKpiScorecard`, `WithdrawRiskRegister`, `WithdrawBenefitProfile` |

## In this repo

- The saga: [`ProjectInitiationSaga`](../../src/Services/StrategyOps.Projects.Api/Features/Sagas/ProjectInitiationSaga.cs)
- Compensating consumers: e.g. [`WithdrawRiskRegisterConsumer`](../../src/Services/StrategyOps.Risk.Api/Features/Consumers/WithdrawRiskRegisterConsumer.cs)
- The rule that makes failure real rather than simulated: [`PortfolioBenefitPolicy`](../../src/Services/StrategyOps.Benefits.Api/Domain/PortfolioBenefitPolicy.cs)
- Verified live: a £900k project (forecast £1.26M, over the £1M ceiling) reached
  `InitiationFailed` in 2 seconds, and the scorecard, register and profile all returned 404

## Follow-up probes

**"What if a compensation itself fails?"**
> It is retried like any other message, and dead-lettered if it keeps failing — at which point
> it needs a human, because you now have a genuinely inconsistent system. This is why
> compensations should be simple and idempotent. Mine delete a row and confirm; there is not
> much to go wrong.

**"What if the action cannot be undone — an email, a payment?"**
> Then you re-order the saga so irreversible steps go **last**, or you use a
> semantic compensation: not "unsend the email" but "send an apology", not "reverse the
> payment" but "issue a refund". The business decides what the compensation *is*; that is the
> part that cannot be solved in code.

**"Can you avoid needing a saga at all?"**
> Often, and it is the better answer when available: if two things must be atomic, put them in
> the same aggregate and therefore the same service. My aggregate boundaries *caused* this
> distributed transaction. A saga is the price of a boundary you have decided is worth it —
> not a pattern to reach for by default.
