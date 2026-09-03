# 13. What is the Saga pattern?

*Asked by: EPAM, Deloitte, Accenture, Cognizant, Virtusa*

## The 60-second answer

> A saga is a sequence of local transactions across services, where each step has a
> **compensating action**, so a failure part-way through can be unwound by running the
> compensations for the steps that already succeeded.
>
> It is the answer to "there is no distributed transaction". Instead of all-or-nothing
> atomicity, you get eventual consistency plus a defined way to get back to a sensible state.
>
> The thing I would stress is that the forward path is the easy bit. Nearly all the complexity
> is failure handling, and there are three failure modes, not one — a step refusing, a step
> never answering, and a step answering **late**, after compensation has already started. That
> last one is the one people forget, and it quietly orphans records for months.

## The shape of it

```
ProvisionKpiScorecard ──→ KpiScorecardProvisioned ──┐
ProvisionRiskRegister ──→ RiskRegisterProvisioned ──┼─→ all three? ─→ ActivateProject
RegisterBenefitProfile ─→ BenefitProfileRegistered ─┘

any failure or timeout ─→ Withdraw* to the legs that succeeded
                       ─→ (wait for every withdrawal to confirm)
                       ─→ FailProjectInitiation
```

## The three failure modes

**1. A leg refuses.** Benefits rejects a forecast over the portfolio ceiling. The saga sends
`WithdrawKpiScorecard` and `WithdrawRiskRegister`, waits for both confirmations, and only then
fails the project.

**2. A leg never answers.** A scheduled 30-second timeout fires and compensates. A saga without
a timeout accumulates instances stuck forever — and you will not notice until someone asks why
a project has been `Initiating` since March.

**3. A leg answers late.** The confirmation arrives *after* compensation began. It is caught in
the `Compensating` state and immediately followed by a withdrawal. Without this, the scorecard
survives, attached to a project that never activated.

## Two rules that are not obvious

**Every participant must always answer.** `WithdrawRiskRegisterConsumer` confirms even when
there was nothing to withdraw. Answering only when there was work to do hangs the saga until
its timeout — and this was a real bug in my first version, caught by a test.

**A business refusal is an event, not an exception.** Thrown, it would be retried five times
and dead-lettered, and the saga would wait for an answer that never comes. So
`RegisterBenefitProfileConsumer` catches the domain exception and publishes
`BenefitProfileRegistrationFailed`.

## Saga state must be persisted

The saga's state lives in the Projects database with optimistic concurrency. Two reasons:

- **it must survive a restart** — an orchestrator that forgets which legs succeeded cannot
  compensate, which is worse than having no orchestrator;
- **three confirmations can land at the same instant** — without a concurrency token, the last
  write silently discards the other two flags.

## In this repo

- [`ProjectInitiationSaga`](../../src/Services/StrategyOps.Projects.Api/Features/Sagas/ProjectInitiationSaga.cs) and [`ProjectInitiationState`](../../src/Services/StrategyOps.Projects.Api/Features/Sagas/ProjectInitiationState.cs)
- Ten tests covering all three failure modes: [`ProjectInitiationSagaTests`](../../tests/StrategyOps.Messaging.Tests/Saga/ProjectInitiationSagaTests.cs)

## Follow-up probes

**"How is it different from a distributed transaction?"**
> A distributed transaction is atomic and isolated — nobody sees the intermediate state. A saga
> is neither: each step commits and is visible immediately. So other services *can* observe a
> half-finished saga, which is why the project sits in an explicit `Initiating` stage rather
> than pretending to be `Active`.

**"What about isolation — can something read the half-done state?"**
> Yes, and that is the saga's real weakness. The mitigations are semantic locks (the
> `Initiating` stage *is* one — the aggregate refuses most operations while in it),
> commutative updates, and simply designing so a partly-initiated project is not harmful to
> observe.

**"Orchestration or choreography?"**
> Q14 — and this repo has one of each so they can be compared directly.
