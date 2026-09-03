# 3. How do microservices communicate with each other?

*Asked by: Cognizant, Globant, Virtusa, EPAM, Deloitte, TCS*

## The 60-second answer

> Two families: **synchronous request/response** — usually HTTP or gRPC, where the caller waits
> — and **asynchronous messaging** over a broker, where the caller does not.
>
> My rule is: **queries go synchronously, state changes go asynchronously.** If I need an
> answer right now to serve a request, I call. If I am telling the rest of the system something
> happened, I publish an event and do not care who listens.
>
> In StrategyOps the gateway's aggregation endpoint calls five services over HTTP, because a
> user is waiting for a screen. But when a risk is escalated, the Risk service publishes
> `RiskEscalated` to RabbitMQ and returns immediately — three other services react on their own
> time. If I had made that synchronous, escalating a risk would fail whenever the Issues
> service was down, and Risk would be as available as its least available dependency.

## The options in practice

| Style | Transport | Good for | The catch |
|---|---|---|---|
| Sync request/response | HTTP/REST | queries, anything a user waits for | temporal coupling — both must be up |
| Sync, high performance | gRPC | chatty internal calls, streaming | binary, less debuggable, needs tooling |
| Async point-to-point | queue (command) | "do this", exactly one handler | still one logical owner |
| Async publish/subscribe | topic/exchange (event) | "this happened", N listeners | no one knows who reacts |

## In this repo

- **Sync:** [gateway aggregation](../../src/Gateway/StrategyOps.Gateway/Features/PortfolioOverview/PortfolioOverview.cs) — five parallel HTTP calls, each with retry, breaker and timeout
- **Async events:** [`RiskEscalated`](../../src/BuildingBlocks/StrategyOps.Contracts/V1/Risks/RiskEvents.cs) → consumed independently by Issues, Projects and Benefits
- **Async commands:** [saga commands](../../src/BuildingBlocks/StrategyOps.Contracts/V1/Sagas/InitiationCommands.cs) — one handler each, and the sender is waiting for an answer
- **Transport wiring:** [`MessagingExtensions`](../../src/BuildingBlocks/StrategyOps.BuildingBlocks/Messaging/MessagingExtensions.cs)

## The distinction that gets probed: command vs event

- An **event** is past tense and states a fact: `RiskEscalated`. Published. Zero or many
  subscribers. The publisher does not know or care who reacts.
- A **command** is imperative and asks for something: `ProvisionRiskRegister`. Sent. Exactly
  one handler. The sender wants to know how it went.

Getting this wrong is the most common design error I see. A "command" with three handlers is
really an event with a misleading name, and it means three services are now coupled to a
sender that thinks it is talking to one.

## Follow-up probes

**"Why not just have everything call everything over HTTP?"**
> Availability multiplies. If five services each have 99.9% uptime and a request needs all
> five synchronously, the chain is about 99.5% — five times worse. Asynchronously, a consumer
> being down means the message waits, not that the request fails.

**"REST or gRPC internally?"**
> gRPC for internal chatty or high-volume calls — it is faster, and the contract is generated
> rather than hoped for. REST at the edge, because everything speaks it and it is debuggable
> with curl. I used REST throughout here because readability was the point.

**"How does a service know another service's address?"**
> Service discovery — see Q11. Never hard-coded, because addresses change on every deploy.
