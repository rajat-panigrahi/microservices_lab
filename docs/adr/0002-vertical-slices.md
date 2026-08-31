# ADR 0002 — Vertical slices instead of layered projects

**Status:** Accepted · **Date:** 2026-08-31

## Context

The conventional .NET enterprise layout gives each service four projects —
`Domain`, `Application`, `Infrastructure`, `Api` — and organises the files inside
them by technical role. Nine services would mean thirty-six projects, and adding
one use case would mean touching four of them.

## Decision

Each service is **one project**, organised by feature:

```
StrategyOps.Projects.Api/
  Features/
    CreateProject/      CreateProject.cs   (command, validator, handler, endpoint)
    SubmitForInitiation/
    SetProjectHealth/
    GetProject/
  Domain/               the aggregate, genuinely shared across slices
  Infrastructure/       DbContext and migrations
```

A slice holds its command, its validation rules, its handler and its HTTP endpoint
together, usually in one file. Adding "close a project" means adding one folder.

`Domain/` stays shared inside the service, because the aggregate really is common
to every slice — it is the thing enforcing the invariants. `Infrastructure/` stays
shared because there is one database.

Endpoints, handlers and validators are discovered by assembly scan
(`AddEndpoints`, `AddSliceHandlers`, `AddValidatorsFromAssembly`), so `Program.cs`
never grows a line when a feature is added.

## Why this matters for microservices specifically

This is the part worth saying out loud in an interview: **a vertical slice is an
extraction seam.** When a service grows too big and needs splitting, the question
is "which slices move?" — and because a slice already owns its full path from HTTP
to database, moving it means moving a folder and pointing it at its own store. A
layered codebase gives you no such seam: the use case is smeared across four
projects, and every extraction is a refactor first.

That is the same reasoning that decides service boundaries in the first place, one
level down. Slices and bounded contexts are the same idea at different scales.

## Consequences

- Small type names (`Command`, `Handler`, `Endpoint`) repeat across feature
  namespaces. That is intended; `.editorconfig` silences IDE0130 accordingly.
- Cross-cutting concerns cannot live in a "Common" layer by accident — they have
  to be deliberate enough to go into `StrategyOps.BuildingBlocks`.
- There is no `IProjectRepository`. EF Core's `DbContext` is already a unit of work
  plus a repository; wrapping it adds a layer that only forwards calls. If asked
  "why no repository pattern?", the answer is that the pattern's benefit is
  swapping persistence, and a service that owns its own database rarely does.
