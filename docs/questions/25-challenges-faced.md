# 25. What challenges have you faced while working with microservices?

*Asked by: Virtusa, Cognizant, EPAM, Deloitte*

## The 60-second answer

> The pattern I keep hitting is that **the bugs are not in the business logic — they are in the
> seams**, and almost none of them are caught by unit tests. Every real problem I hit building
> StrategyOps came from running the system, not from a red test.
>
> The one I would lead with: a saga participant that stayed **silent** when it had nothing to
> undo. `WithdrawRiskRegisterConsumer` returned early if there was no register to withdraw —
> which looks correct, and hangs the entire saga until its 30-second timeout, because the
> orchestrator is waiting for a confirmation that never comes. The fix is a rule I would now
> apply everywhere: **every saga participant must always answer, even when it did nothing.**

## Seven real ones, with the fix

**1. A participant that stayed silent hung the orchestrator.**
As above. Caught by a messaging test asserting the confirmation, not by reading the code — it
reads as sensible defensive programming. *Lesson: in a saga, silence is a failure mode.*

**2. A business refusal thrown as an exception.**
Benefits rejecting a forecast over the portfolio ceiling was originally an exception. It got
retried five times and dead-lettered, and the saga waited for its timeout instead of
compensating promptly. *Lesson: a business refusal is an event, not a transient fault.*

**3. A projection that could not tell a new reading from a recovery.**
The KPI RAG buckets were maintained as counters — increment the new bucket, decrement the old.
It broke the moment a KPI recovered, because the event carries the new status but not the old,
and the projection had no way to know which bucket to decrement. Fixed by keeping a per-KPI
status row and **recomputing** the counts every time. *Lesson: counters drift silently and
forever; derived values are self-healing.*

**4. A correlation id that vanished at the first hop.**
The gateway generated an id when the caller sent none, logged it, and then it disappeared — a
reverse proxy forwards the *incoming* headers, and nothing had written the new id back onto the
request. The chain started and stopped at the gateway. *Lesson: propagation has to be tested
end to end; a correlation id that is only logged locally is a request id.*

**5. Provider behaviour that is invisible until it runs.**
SQLite cannot `ORDER BY` a `DateTimeOffset`, so listing issues by SLA deadline returned 500 —
LINQ that compiled, passed review, and works fine against SQL Server. Separately, only an
INTEGER primary key autoincrements in SQLite, which forced the outbox's ordering column to
become its key. *Lesson: "works on my machine's database" is a real class of bug, and the fix
belongs in shared infrastructure — a value converter applied by convention — not in each query.*

**6. A bug that appeared in one service and not another, because of a string length.**
An instance id was truncated with `[..48]`, which throws if the string is shorter. Whether it
threw depended on the service name: `issues-api` is two characters shorter than
`projects-api`. *Lesson: identical shared code can fail in one service and not its neighbour,
and that asymmetry is very hard to reason about from a stack trace.*

**7. The same identity bug, twice, in two different places.**
After securing every endpoint, the gateway's aggregation endpoint called four services
anonymously and got four 401s. I fixed it with a token-forwarding handler — and then the
read-model **rebuild** endpoint turned out to have exactly the same bug, found weeks later by
running it. *Lesson: when you change a cross-cutting concern like authentication, the question
is not "did I fix the caller I was looking at" but "which other outbound clients exist?" A
grep for `AddHttpClient` would have found both in seconds.*

## A bug the environment handed me for free

Midway through the final verification run, RabbitMQ died because the container restarted. The
services stayed healthy, the HTTP writes kept succeeding, and two events sat unpublished in the
outbox. When the broker came back, **the saga completed one second later** with nothing lost.

I could not have staged a better demonstration of why the outbox exists. Without it, those two
events would have been published into a dead connection and gone.

## The structural challenges, beyond individual bugs

**Debugging has no stack trace.** The replacement is correlation ids and tracing, and they have
to be in place *before* you need them. Retrofitting observability during an incident is not a
thing you can do.

**Eventual consistency is an explaining problem as much as an engineering one.** The mechanics
are manageable; telling a stakeholder their dashboard is a second stale, and having them accept
it, is harder. Showing the staleness — a last-updated column — works better than arguing.

**Testing needs a different shape.** Unit tests cover the least risky part. The messaging tier
is the second-largest tier in this repo because that is where the risk actually lives:
redelivery, out-of-order arrival, compensation, late confirmations.

**Failure handling is most of the code.** The initiation saga's forward path is short. Nearly
all its complexity is three failure modes — a leg refusing, a leg never answering, and a leg
answering *late*, after compensation started. That third one is the one people forget, and it
silently orphans records for months.

**Local development gets heavier.** Nine services, a broker and a database. I mitigated it:
every test tier runs with **zero infrastructure**, and the whole suite is ~11 seconds. A test
suite you cannot run without Docker is a test suite that stops being run.

## If I did it again

- **Start with a modular monolith** and extract only when a specific pain appears.
- **Build the platform first** — outbox, inbox, correlation, health, resilience — before the
  second service. Retrofitting them across nine services is much worse than having them from
  the start, which is why they are the phase-1 commit here.
- **Design events to be idempotent**, not just consumers. `BenefitRealised` carries the running
  total as well as the delta, so a redelivery sets the same number instead of adding twice. One
  contract decision removed a whole class of bug.
- **Write the failure test first.** Every one of the six above was found by a test I wrote
  *after* the bug. The saga tests I wrote failure-first are the ones that found nothing later,
  because the bugs were never written.

## Follow-up probes

**"What would you tell a team about to start?"**
> Name the pain first. If you cannot say which release is blocked by which team, or which
> component needs to scale differently, you are buying the costs without the benefits.

**"What surprised you most?"**
> How much of the difficulty is *operational* rather than architectural. The patterns are
> well documented and not hard to implement. Knowing that a queue is quietly growing at 3am,
> or that a saga has been stuck since Tuesday, is the part that takes real work.
