# 14. Saga choreography vs orchestration

*Asked by: EPAM, Deloitte, Accenture, Cognizant*

## The 60-second answer

> **Orchestration** has a coordinator that tells each service what to do and waits for answers
> — one place that knows the whole flow. **Choreography** has no coordinator: each service
> reacts to events and publishes its own, and the flow is an emergent property.
>
> The trade is **visibility versus coupling**. Orchestration gives you one file that describes
> the process and a state table you can query to find stuck instances; the cost is that the
> orchestrator knows every participant by name. Choreography gives you services that know
> nothing about each other and a fourth reaction that needs no changes anywhere; the cost is
> that no file describes what actually happens.
>
> I built one of each in StrategyOps deliberately. Project initiation is orchestrated because
> the outcome must be all-or-nothing. Risk escalation is choreographed because the reactions
> are independently valuable — a benefit being flagged without the project's RAG moving is
> merely incomplete, not broken.

## The two flows, side by side

| | Orchestration — project initiation | Choreography — risk escalation |
|---|---|---|
| Message style | commands, imperative, one handler, *sent* | events, past tense, N subscribers, *published* |
| Where the flow lives | one file, `ProjectInitiationSaga.cs` | nowhere — you find it by grepping for consumers |
| Adding a participant | edit the saga | add a consumer; change nothing else |
| "What happened?" | query the saga state table | correlate logs across four services |
| Compensation | explicit, coordinated, ordered | each service decides for itself |
| Coupling | coordinator knows everyone | participants know only the event |
| Failure to diagnose | one place to look | distributed detective work |

## The rule I use

**Orchestrate when the outcome must be all-or-nothing. Choreograph when the reactions are
independently valuable.**

A project with a risk register but no benefit profile is *broken* — orchestrate. A benefit
flagged at risk while the project's RAG has not moved yet is *incomplete* — choreograph.

## The choreographed chain, concretely

```
POST /risks/{id}/escalate
  Risk publishes RiskEscalated          ← knows nothing about what follows
    → Issues raises an issue            → publishes IssueRaised
        → Projects drops RAG to Red
        → Benefits flags the forecast at risk
  ...and later:
    → Issues resolves the issue         → publishes IssueResolved
        → Risk closes the originating risk   ← the loop closes
```

Four services, no coordinator, and adding a fifth reaction tomorrow requires changing none of
them. That is the appeal. The cost shows up the first time someone asks "what happens when a
risk escalates?" — the answer is not in any one file.

## In this repo

- Orchestrated: [`ProjectInitiationSaga`](../../src/Services/StrategyOps.Projects.Api/Features/Sagas/ProjectInitiationSaga.cs)
- Choreographed: [`EscalateRisk`](../../src/Services/StrategyOps.Risk.Api/Features/EscalateRisk/EscalateRisk.cs) → [`RaiseIssueOnRiskEscalatedConsumer`](../../src/Services/StrategyOps.Issues.Api/Features/Consumers/RaiseIssueOnRiskEscalatedConsumer.cs) → [`DropHealthOnIssueRaisedConsumer`](../../src/Services/StrategyOps.Projects.Api/Features/Consumers/DropHealthOnIssueRaisedConsumer.cs)
- The full argument: [`docs/adr/0005-saga-orchestration-and-choreography.md`](../adr/0005-saga-orchestration-and-choreography.md)

## Follow-up probes

**"Which is more common in practice?"**
> Choreography by accident, orchestration by decision. Systems drift into choreography because
> publishing an event is the easy next step, and then nobody can explain what happens when an
> order is placed. That drift is the strongest argument for orchestrating anything with a
> business-critical outcome.

**"Can you mix them?"**
> Yes, and this system does. Within one flow, mixing is where it gets confusing — a saga that
> sends two commands and then hopes an event happens is the worst of both.

**"How do you debug a choreographed flow?"**
> Correlation ids, and nothing else works. In this repo one grep across six services returns
> the whole chain in order. Without that, choreography is close to undebuggable in production.
