# ADR 0001 — Tooling and framework choices

**Status:** Accepted · **Date:** 2026-08-31

## Context

This repository is a learning lab: the code has to be readable, runnable with as
little setup as possible, and defensible in an interview. Several popular .NET
libraries have changed licence in the last two years, and "why did you pick X?"
is a normal follow-up question, so the choices are recorded here.

## Decisions

### .NET 8 LTS, not .NET 9/10

The build environment installs the SDK from the Ubuntu archive, which carries
8.0.x. .NET 8 is also still the most common target in enterprise .NET shops, so
it is the more useful thing to have built against.

EF Core is the exception: it sits on the **9.0** line, because MassTransit 8.5
depends on `Microsoft.EntityFrameworkCore.Relational` 9. EF Core 9 targets
`net8.0`, so this is supported and not a fudge.

### MassTransit 8.x, held deliberately

MassTransit **v9 moved to a commercial licence**. v8 remains free under Apache 2.0
and is what the overwhelming majority of existing .NET systems run. Pinned in
`Directory.Packages.props` with a comment so nobody "helpfully" upgrades it.

Interview value: knowing this is a real signal of having worked with the library
rather than having read about it.

### Shouldly, not FluentAssertions

FluentAssertions **8.x requires a paid licence for commercial use**. Shouldly is
MIT and reads almost identically (`value.ShouldBe(expected)`). Same reasoning as
MassTransit — avoid a licence trap in a repo meant to be copied from.

### No MediatR

MediatR also went commercial. More importantly, it is not the pattern — it is a
convenience over the pattern. Handlers here are plain classes registered in DI
and called directly from minimal-API endpoints:

```csharp
app.MapPost("/risks", async (RaiseRiskCommand cmd, RaiseRiskHandler handler, CancellationToken ct)
    => (await handler.HandleAsync(cmd, ct)).ToHttpResult());
```

One less indirection to trace, no licence question, and it makes the vertical
slice boundary obvious. If a team wants MediatR, the handlers drop into it
unchanged — which is the point.

### SQLite by default, PostgreSQL optionally

Database-per-service is a core microservices principle, and the lab has to
demonstrate it. Nine PostgreSQL containers is a hostile first-run experience, so
each service gets its own SQLite **file** — still genuinely separate databases,
still no shared schema, but zero setup. `docker-compose.yml` switches the
connection strings to PostgreSQL to show the production shape.

### Central package management

`Directory.Packages.props` pins every version in one place, so ten projects
cannot drift apart. `TreatWarningsAsErrors` is on in `Directory.Build.props`.

## Consequences

- Nothing in this repo requires a commercial licence to run or to copy from.
- Upgrading MassTransit past 8.x is a licensing decision, not a routine bump.
- The SQLite default means transactions behave slightly differently from
  PostgreSQL under concurrency; the outbox tests pin the behaviour that matters.
