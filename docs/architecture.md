# StrategyOps — architecture

## The domain, and why it was chosen

Strategic Objectives are delivered by **Projects**, measured by **KPIs**, threatened by
**Risks** — which materialise into **Issues** — and justified by **Benefits**.

That domain is not decoration. It generates the three problems microservices interviews are
actually about:

| The domain produces… | …which forces |
|---|---|
| Initiating a project touches Projects, KPI, Risk and Benefits at once | a distributed transaction → **saga with compensation** |
| A risk materialises → an issue is raised → project health drops → a benefit is at risk | **event choreography** across four services |
| One portfolio dashboard reading across five databases | **CQRS** read model + **eventual consistency** |

## Services

```
                      ┌──────────────────────────┐
   client ───────────▶│  Gateway (YARP)  :5100   │
                      │  routing · edge JWT      │
                      │  rate limit · aggregation│
                      └────┬──────────────┬──────┘
                           │              │
        ┌──────────────────┴───┐      ┌───┴─────────────────┐
        │                      │      │                     │
   ┌────▼─────┐  ┌──────────┐  │  ┌───▼──────┐  ┌─────────┐ │
   │ Projects │  │   KPI    │  │  │ Identity │  │Discovery│ │
   │  :5101   │  │  :5102   │  │  │  :5107   │  │  :5108  │ │
   └────┬─────┘  └────┬─────┘  │  └──────────┘  └─────────┘ │
        │             │        │                            │
   ┌────▼─────┐  ┌────▼─────┐  │  ┌──────────┐              │
   │   Risk   │  │ Benefits │  │  │Reporting │◀─────────────┘
   │  :5103   │  │  :5105   │  │  │  :5106   │  read model
   └────┬─────┘  └────┬─────┘  │  └────▲─────┘  + SignalR
        │             │        │       │
   ┌────▼─────┐       │        │       │
   │  Issues  │       │        │       │
   │  :5104   │       │        │       │
   └────┬─────┘       │        │       │
        │             │        │       │
        └─────────────┴────────┴───────┘
                      │
              ╔═══════▼═══════╗
              ║   RabbitMQ    ║   every state change travels here
              ╚═══════════════╝
```

Each service owns its own database. **No service reads another's tables** — that single
constraint is the architecture, and everything else exists to cope with it.

## Context map

```mermaid
graph LR
    subgraph Delivery
        P[Projects<br/>lifecycle + saga]
    end
    subgraph Measurement
        K[KPI<br/>scorecards, RAG]
    end
    subgraph "Risk & Issue"
        R[Risk<br/>might happen]
        I[Issues<br/>has happened]
    end
    subgraph Value
        B[Benefits<br/>forecast vs actual]
    end
    subgraph Read
        Rep[Reporting<br/>owns no truth]
    end

    P -->|ProjectInitiationRequested| K
    P -->|saga commands| R
    P -->|saga commands| B
    R -->|RiskEscalated| I
    I -->|IssueRaised| P
    I -->|IssueRaised| B
    I -->|IssueResolved| R
    K -->|KpiBreached| B
    P & K & R & I & B -.->|all events| Rep
```

Risk and Issues are separate contexts on purpose. They look almost identical — a title, an
owner, a status, a severity — but a risk register is reviewed monthly by a risk owner and an
issue is chased daily against an SLA. **Different rate and reason for change.** The similarity
of the tables is a trap.

## Flow 1 — project initiation (orchestrated)

```mermaid
sequenceDiagram
    participant U as User
    participant P as Projects + Saga
    participant K as KPI
    participant R as Risk
    participant B as Benefits

    U->>P: POST /projects/{id}/submit-for-initiation
    P-->>U: 200 { stage: "Initiating" }
    Note over P: honest intermediate state,<br/>not a fake "Active"

    par three commands in parallel
        P->>K: ProvisionKpiScorecard
        P->>R: ProvisionRiskRegister
        P->>B: RegisterBenefitProfile
    end

    K-->>P: KpiScorecardProvisioned
    R-->>P: RiskRegisterProvisioned
    B-->>P: BenefitProfileRegistered
    Note over P: all three? → ActivateProject
    P->>P: Project → Active
```

When Benefits refuses — a forecast over the portfolio ceiling:

```mermaid
sequenceDiagram
    participant P as Projects + Saga
    participant K as KPI
    participant R as Risk
    participant B as Benefits

    K-->>P: KpiScorecardProvisioned ✓
    R-->>P: RiskRegisterProvisioned ✓
    B-->>P: BenefitProfileRegistrationFailed ✗

    Note over P: compensate the legs that succeeded
    P->>K: WithdrawKpiScorecard
    P->>R: WithdrawRiskRegister
    K-->>P: KpiScorecardWithdrawn
    R-->>P: RiskRegisterWithdrawn
    Note over P: only NOW is the project failed
    P->>P: Project → InitiationFailed (reason recorded)
```

Measured: 2 seconds from submit to `InitiationFailed`, with the scorecard, register and profile
all returning 404 afterwards.

## Flow 2 — risk escalation (choreographed)

```mermaid
sequenceDiagram
    participant U as Risk owner
    participant R as Risk
    participant I as Issues
    participant P as Projects
    participant B as Benefits

    U->>R: POST /risks/{id}/escalate
    R-->>U: 200 (≈40ms)
    R->>R: publish RiskEscalated
    Note over R: knows nothing about what follows

    R->>I: RiskEscalated
    I->>I: raise Issue, publish IssueRaised
    I->>P: IssueRaised → health Amber/Red
    I->>B: IssueRaised → benefit AtRisk

    Note over I,R: later…
    I->>R: IssueResolved → close the originating risk
```

No coordinator. Adding a fifth reaction tomorrow requires changing none of these services —
and that is exactly the trade: no file describes this flow, and you find it by searching for
consumers.

## Flow 3 — the read model (CQRS)

```mermaid
graph LR
    P[Projects] -->|5 events| RM
    K[KPI] -->|5 events| RM
    R[Risk] -->|4 events| RM
    I[Issues] -->|2 events| RM
    B[Benefits] -->|4 events| RM
    RM[(portfolio_scorecards<br/>one flat row per project)] --> Q[GET /reporting/portfolio<br/>one indexed SELECT]
    RM --> S[SignalR → dashboard]
```

Seventeen projections. The row owns no truth — every column is a copy — and
`POST /reporting/rebuild` discards and rebuilds it from the source services.

## The building blocks every service shares

| Block | Solves |
|---|---|
| **Outbox** | the dual-write problem: entity + message commit in one local transaction |
| **Inbox** | at-least-once delivery: exactly-once *processing*, keyed on (MessageId, Consumer) |
| **Correlation** | one id across HTTP, the broker, and every log line |
| **Resilience** | timeout → retry+jitter → circuit breaker → per-attempt timeout, per dependency |
| **Auth** | JWT validation with a secure-by-default fallback policy |
| **Discovery** | lease-based registry + client-side load balancing |
| **Observability** | Serilog, OpenTelemetry, split liveness/readiness probes |

## Deliberate simplifications

Stated plainly, because being able to name them is worth more than pretending:

| Here | Production |
|---|---|
| SQLite file per service | PostgreSQL per service |
| HS256 shared signing key | RS256 + JWKS, via Entra ID / Duende / Keycloak |
| Password grant | authorization code + PKCE |
| Quartz in-memory scheduler | durable store, or RabbitMQ's delayed-exchange plugin |
| Home-grown service registry | Consul, or nothing at all on Kubernetes |
| Migrations on service startup | a migration Job that runs before the rollout |
| Saga commands published, not sent | endpoint conventions + `Send`, so a second consumer fails loudly |

## Where to read next

- [`00-start-here.md`](00-start-here.md) — a reading path
- [`questions/`](questions) — the 25 interview answers, each linked to code
- [`adr/`](adr) — nine decision records, each with the trade-off
- [`testing.md`](testing.md) — the four test tiers and why the middle is fat
- [`demo-script.md`](demo-script.md) — see all of it run
