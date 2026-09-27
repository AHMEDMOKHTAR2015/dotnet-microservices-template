# Architecture Playbook: DDD × Vertical Slice × Clean Architecture for .NET Microservices

This playbook documents **how this codebase is built**, so you can apply the same patterns to your next project. It is taken from the code under [`src/`](../../src). Every rule points to the file it came from. Nothing here is general "best practice" unless the project actually does it.

When the code is inconsistent (several services solve the same problem differently), the playbook says so. It names the variant with the most support in the code and records the others in [11 · What not to copy](11-what-not-to-copy.md).

> **About this copy.** This playbook documents the *reference implementation* the template was extracted from:
> the Articles system in the DotNetLabX `dotnet-microservices` course repository (MIT, see `LICENSE`). Links into `../../src/...` point to that
> repository's code and won't resolve here. The patterns apply unchanged; only the names are generalized. See the
> **"Reference → template names"** table in the root [`CLAUDE.md`](../../CLAUDE.md). The runnable example of every pattern in
> this repo is `src/Services/Orders`.

---

## How to read this

| If you want to… | Read |
|---|---|
| Understand the architecture | [01](01-architecture.md) → [02](02-domain-modeling.md) → [03](03-feature-slices.md) → [04](04-service-communication.md) → [05](05-persistence.md) → [06](06-cross-cutting.md) |
| Start a new project with these patterns | [10 · Implementation checklist](10-implementation-checklist.md), with [08](08-style-guide.md) and [09](09-decision-framework.md) open beside it |
| Add a feature to an existing service | [03 · Feature slices](03-feature-slices.md) + [02 · Domain modeling](02-domain-modeling.md) |
| Know *why* something is the way it is | [09 · Decision framework](09-decision-framework.md) |
| Avoid copying accidents | [11 · What not to copy](11-what-not-to-copy.md) |

## Contents

1. [Architecture: solution topology, layers, and how the three patterns fit](01-architecture.md)
2. [Domain modeling (DDD tactical patterns)](02-domain-modeling.md)
3. [Feature slices (Vertical Slice + CQRS)](03-feature-slices.md)
4. [Service communication: events, gRPC, consumers, gateway](04-service-communication.md)
5. [Persistence: EF Core, repositories, seeding, caching, Redis, read models](05-persistence.md)
6. [Cross-cutting concerns: errors, validation, logging, config, DI, security, modules](06-cross-cutting.md)
7. [Testing: what exists and what the design makes testable](07-testing.md)
8. [Code style guide](08-style-guide.md)
9. [Decision framework](09-decision-framework.md)
10. [Implementation checklist for a new project](10-implementation-checklist.md)
11. [What not to copy](11-what-not-to-copy.md)

## Custom agents built from this playbook

The playbook is also packaged as six Claude Code subagents in [`.claude/agents/`](../../.claude/agents). Each one is self-contained, so it works in a repo without these docs. When the repo's `docs/playbook/` or `.claude/skills/` exist, the agent reads them for depth.

| Agent | Role | Edits code? |
|---|---|---|
| [`dotnet-architect`](../../.claude/agents/dotnet-architect.md) | Decisions (D1–D18) + ordered implementation plan naming which agent does each step | No |
| [`dotnet-service-scaffolder`](../../.claude/agents/dotnet-service-scaffolder.md) | New service / solution skeleton: projects, DI, `Program.cs`, persistence setup | Yes |
| [`dotnet-domain-modeler`](../../.claude/agents/dotnet-domain-modeler.md) | Aggregates, invariants, value objects, state machine, domain events, EF mapping | Yes |
| [`dotnet-slice-developer`](../../.claude/agents/dotnet-slice-developer.md) | Vertical slices in any of the three endpoint variants | Yes |
| [`dotnet-integration-engineer`](../../.claude/agents/dotnet-integration-engineer.md) | Integration events, consumers, gRPC, file handoff | Yes |
| [`dotnet-architecture-reviewer`](../../.claude/agents/dotnet-architecture-reviewer.md) | Runs the verification greps + checklist, reports findings by severity | No |

Typical flow: architect → scaffolder (new service only) → domain-modeler → slice-developer → integration-engineer → reviewer. To use them in every project, copy them to your user folder: `cp .claude/agents/dotnet-*.md ~/.claude/agents/`.

---

## The mental model on one page

The system is an **article publishing lifecycle**: draft → submission → review → production → publish. Each lifecycle stage is a microservice with its own database. A read-only hub collects the latest state from all of them.

Four ideas work together. Each answers a *different* question, which is why they don't conflict:

| Idea | Question it answers | Where you see it |
|---|---|---|
| **Clean Architecture** (practical variant) | *Which project may reference which?* | Four projects per service: `.API` → `.Application` → `.Persistence` → `.Domain` |
| **Vertical Slice Architecture** | *How is code grouped inside a layer?* | `Features/{Area}/{Operation}/`, one folder per use case |
| **Domain-Driven Design** | *Where do business rules live?* | `.Domain`: aggregates, value objects, domain events, state machines |
| **CQRS** (via MediatR or FastEndpoints) | *How does a request travel?* | Command/Query record → pipeline → handler → aggregate |

### Lifecycle of one write request

```
HTTP POST /api/articles/42:approve
  │
  ▼
Endpoint (API)            route binding, declarative auth gate: role + "is this user on article 42?"
  │  sender.Send(command with { ArticleId = 42 })
  ▼
Pipeline (Blocks.MediatR) AssignUserId → Validation → Logging
  ▼
Handler (Application)     orchestrates: load aggregate, fetch foreign data (gRPC) if missing
  ▼
Aggregate (Domain)        article.Approve(editor, command, stateMachineFactory)
  │                         checks invariants + legal stage transition, mutates, raises domain events
  ▼
Repository.SaveChangesAsync (Persistence)
  ▼
EF SaveChangesInterceptor dispatches domain events in-process
  ├─► {Effect}On{Event}Handler                 e.g. send an email, write a timeline entry
  └─► PublishIntegrationEventOn{Event}Handler  maps to a flat DTO, publishes to RabbitMQ
          ▼
        Consumers in other services (Review, Journals, ArticleHub) update their own databases
```

### The six services are six variants

The services deliberately solve the same problems in different ways; the codebase is a teaching reference. Pick the column that fits your service, not a mix.

| Service | Role | Endpoint framework | MediatR | Application project | Storage | Domain events dispatched by | Best example of |
|---|---|---|---|---|---|---|---|
| **Submission** | Write side, first stage | Minimal APIs | ✔ requests + events | ✔ | SQL Server + Mongo GridFS | post-save interceptor | The canonical MediatR slice; the aggregate |
| **Review** | Write side | Carter | ✔ requests + events | ✔ | SQL Server + 2× GridFS | post-save interceptor | Domain organized by aggregate; stage-handoff consumer |
| **Production** | Write side | FastEndpoints | events only | thin | SQL Server + Azure Blob + GridFS | **transactional** interceptor | Handler-in-endpoint; embedded module (ArticleTimeline) |
| **Journals** | CRUD-ish reference data | FastEndpoints | ✘ | ✘ | Redis (Redis.OM) | manual `PublishAsync` | gRPC server; document store without EF |
| **Auth** | Identity | FastEndpoints | ✘ | minimal | SQL Server + ASP.NET Identity | manual `PublishAsync` | gRPC server; JWT issuing |
| **ArticleHub** | Read model | Carter | ✘ | ✘ | PostgreSQL + Hasura GraphQL | none | Pure projection built from events |

Ports: HTTP 4401–4406, HTTPS 4451–4456, in the order Auth, Journals, ArticleHub, Submission, Review, Production.

---

## Provenance

- Built on 2026-09-25 by reading the working tree: every BuildingBlock, Submission end to end, and the key slices, consumers, DI setup, and domain models of the other five services and the three modules.
- Cross-checked against the repo's own verified pattern registry, [`docs/reference-model.md`](../reference-model.md), which was built from an earlier commit. Where the two disagree, the code wins. Example: the registry lists API Gateway port drift, but [`ApiGateway/appsettings.json`](../../src/ApiGateway/appsettings.json) now routes to the correct ports.
- Code excerpts are copied from source and trimmed with `// ...` where lines don't affect the point.
