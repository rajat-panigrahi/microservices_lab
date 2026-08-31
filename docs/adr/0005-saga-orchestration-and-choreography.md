# ADR 0005 — One orchestrated saga, one choreographed chain

**Status:** Accepted · **Date:** 2026-08-31

## Context

Two flows in this system span several services, and both need an answer to
"there is no distributed transaction, so how do we stay consistent?".

They are built **differently on purpose**, so the trade-off can be compared in one
codebase rather than argued about in the abstract.

## The two flows

### Project initiation — orchestrated

`POST /projects/{id}/submit-for-initiation` has to make KPI, Risk and Benefits all
set the project up, and must not leave the project half-configured if any of them
refuses.

`ProjectInitiationSaga` (a MassTransit state machine in the Projects service)
sends three commands in parallel, waits for three answers, and either activates
the project or compensates whatever succeeded.

```
                    ┌── ProvisionKpiScorecard ──→ KPI ──→ KpiScorecardProvisioned ──┐
ProjectInitiation ──┼── ProvisionRiskRegister ──→ Risk ─→ RiskRegisterProvisioned ──┼──→ all three?
   Requested        └── RegisterBenefitProfile → Ben. ──→ BenefitProfileRegistered ─┘      ↓
                                                                                    ActivateProject
                              any failure or timeout ──→ Withdraw* to the legs that succeeded
                                                    ──→ FailProjectInitiation
```

### Risk escalation — choreographed

`POST /risks/{id}/escalate` publishes one fact. Three services react independently:
Issues raises an issue, Projects drops the RAG status, Benefits flags the forecast
at risk. Nothing coordinates them and no file describes the flow.

## Decision

| | Orchestration (initiation) | Choreography (escalation) |
|---|---|---|
| Message shape | commands, imperative, one handler | events, past tense, any number of subscribers |
| Where the flow lives | one file, `ProjectInitiationSaga.cs` | nowhere — you find it by searching for consumers |
| Adding a participant | edit the saga | add a consumer, change nothing else |
| Debugging "what happened?" | query the saga state table | correlate logs across four services |
| Compensation | explicit, coordinated, ordered | each service decides for itself |
| Coupling | coordinator knows all participants | participants know only the event |

The rule this repo follows: **orchestrate when the outcome must be all-or-nothing;
choreograph when the reactions are independently valuable.** A project that ends up
with a risk register but no benefit profile is broken. A benefit that gets flagged
without the project's RAG moving is merely incomplete.

## Three failure modes the saga handles

Most hand-rolled sagas handle only the first.

1. **A leg refuses.** Benefits rejects a forecast above the portfolio ceiling — a
   real business rule, so the demo needs no stubbing. Compensation withdraws the
   scorecard and the register that already succeeded.
2. **A leg never answers.** A scheduled 30-second timeout fires and compensates.
   A saga without a timeout accumulates instances stuck forever.
3. **A leg answers late, after compensation began.** The confirmation is caught in
   the `Compensating` state and immediately followed by a withdrawal. Without this
   the scorecard survives, attached to a project that never activated — an orphan
   that accumulates quietly for months.

Two more details that are easy to get wrong and are pinned by tests here:

- **A participant must always answer.** `WithdrawRiskRegisterConsumer` confirms even
  when there was nothing to withdraw. Answering only when there was work to do hangs
  the saga until its timeout — and the first version of that consumer had exactly
  this bug, caught by `CompensationWithNothingToUndo_StillConfirmsBackToTheSaga`.
- **A business refusal is an event, not an exception.** Thrown, it would be retried
  five times and dead-lettered, and the saga would wait for an answer that never
  comes. `RegisterBenefitProfileConsumer` catches `DomainException` and publishes
  `BenefitProfileRegistrationFailed`.

## Consequences

- Saga state is persisted in the Projects database with optimistic concurrency, so
  it survives a restart and three simultaneous confirmations cannot overwrite each
  other's flags.
- Saga timeouts need a message scheduler. RabbitMQ can only do this natively with
  the delayed-message-exchange plugin, so this uses **Quartz with an in-memory
  store** instead: pending timeouts are lost on restart. Point Quartz at a shared
  database, or install the plugin and use `UseDelayedMessageScheduler()`, to fix
  that.
- The commands are **published** rather than sent to a named endpoint, because
  MassTransit routes each type to its single consumer and this keeps the lab free of
  endpoint address configuration. In production, map endpoint conventions and use
  `Send`, so a command that accidentally gains a second consumer fails loudly.
- `InitiationFailed` is deliberately resubmittable: a transient outage should be
  retryable without recreating the project.
