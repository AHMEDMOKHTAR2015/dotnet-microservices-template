# Starter: .NET microservices template (DDD × Vertical Slice × Clean Architecture)

A ready-to-use solution template, extracted from a production-shaped reference system. It gives you:

- **BuildingBlocks**: domain base types, EF Core plumbing (repository = unit of work, configuration ladder, domain-event interceptors, JSON seeding), MediatR pipeline, FastEndpoints variant, MassTransit/RabbitMQ, JWT + two-layer authorization, exception middleware, correlation IDs, gRPC client helper, Redis.OM and Hasura blocks.
- **Modules**: email (Empty / SMTP / SendGrid) and file storage (MongoDB GridFS / Azure Blob), each swappable with one line.
- **A runnable sample service**, `Orders`, that shows every pattern end to end: aggregate with a table-driven state machine, value object, enum entity, audit trail, vertical slices, validation, role + ownership authorization, domain events, integration event, email side effect, and an initial migration.
- **An API gateway** (YARP), **docker-compose** (SQL Server, RabbitMQ; optional MongoDB, Redis, Postgres, Hasura), a **dev token tool**, and **domain tests**.
- **AI tooling**: six Claude Code agents (`.claude/agents/`) and the architecture playbook (`docs/playbook/`).

Targets **.NET 10**. All package versions are central in `Directory.Packages.props`.

---

## Use it as a `dotnet new` template

```bash
# install once (from this folder)
dotnet new install .

# create a project: every "Starter" (namespaces, projects, solution, images, docs) becomes your name
dotnet new ddd-microservices -n Acme -o Acme
cd Acme
```

Or copy the folder and rename `Starter` yourself.

## Quick start

```bash
dotnet tool restore                                    # dotnet-ef
dotnet build Starter.sln
dotnet test                                            # 14 domain tests, no infrastructure needed

docker compose up -d sqlserver rabbitmq                # local infrastructure
dotnet run --project src/Services/Orders/Orders.API    # migrates OrdersDb on start → https://localhost:4451/swagger
```

Get tokens (there's no identity service yet; the tool signs with the dev key from `appsettings.Development.json`):

```bash
dotnet run --project tools/Starter.DevToken -- --user 1 --roles CUSTOMER        # customer
dotnet run --project tools/Starter.DevToken -- --user 2 --roles ORDERMANAGER    # approver
```

Then in Swagger (**Authorize** → paste the token), or with the Postman collection in `postman/`:

1. `POST /api/orders` as the customer → `201 { id }`
2. `POST /api/orders/{id}/lines` → add a line
3. `POST /api/orders/{id}:submit`
4. `POST /api/orders/{id}:approve` as the manager → publishes `OrderApprovedEvent`, logs the confirmation email
5. `GET /api/orders/{id}` → stage `Approved`, stage history, total

Try the rules: approve a draft (`400`, the state machine forbids it), add the same product twice (`400`, an aggregate invariant), or read another customer's order (`403`, the ownership check).

Everything in containers: `docker compose up -d --build` (API on `:4401`, gateway on `:4400` at `/orders/api/...`).

---

## Add your own service

With Claude Code, the agents drive it:

```
use dotnet-architect to plan a Shipping service that reacts to OrderApprovedEvent
```

Then follow the plan's steps: `dotnet-service-scaffolder` → `dotnet-domain-modeler` → `dotnet-slice-developer` → `dotnet-integration-engineer` → `dotnet-architecture-reviewer`.

By hand: follow `docs/playbook/10-implementation-checklist.md` and mirror `src/Services/Orders`.

**Remove the sample** once you have a real service: delete `src/Services/Orders` and `tests/Orders.Domain.Tests`, run `dotnet sln Starter.sln remove` on their projects, delete `Starter.IntegrationEvents.Contracts/Orders`, and replace the sample vocabulary in `Starter.Abstractions/Enums` and `Starter.Security/Role.cs`.

---

## Decisions baked in

| Area | Choice | Change it in |
|---|---|---|
| Endpoints (sample) | Minimal APIs + MediatR | `Blocks.FastEndpoints` is included for FastEndpoints services |
| Persistence | SQL Server + EF Core 10, singular table names, enums as strings | per-service `DependencyInjection` / `DbContext` |
| Domain events | Post-save interceptor (after commit) | swap to `TransactionalDispatchDomainEventsInterceptor` when handlers must write atomically |
| Messaging | MassTransit **8.x** on RabbitMQ | `Blocks.Messaging` |
| Auth | JWT validated in every service; role + aggregate-ownership policies | `Starter.Security`, per-service `*AccessChecker` |
| Aggregate route key | `{id:int}` | `Blocks.AspNetCore/RouteKeys.cs` |

## Licensing notes

- The template contains code from the DotNetLabX `dotnet-microservices` course repository, **MIT**: see `LICENSE` (keep the notice).
- **MediatR** is pinned to **12.5.0** (Apache-2.0), the last open-source release; 13.x is commercial (Lucky Penny Software). Do not accept a Dependabot/Renovate bump to 13.
- **MassTransit** is pinned to **8.x** (Apache-2.0); 9.x is commercial. Do not accept a bump to 9.
- A transitive pin lifts `System.Security.Cryptography.Xml` to a patched version (high-severity advisories on 10.0.0).

## Known open gaps

Not solved by the reference either, so decide them per project: transactional outbox, consumer retry policy, gRPC exception→status mapping, correlation-ID propagation over gRPC/messages, and resource-level authorization for FastEndpoints attribute routes. Details: `docs/playbook/11-what-not-to-copy.md`.
