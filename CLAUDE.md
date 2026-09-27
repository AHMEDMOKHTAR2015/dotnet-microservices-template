# Starter: DDD × Vertical Slice × Clean Architecture microservices

Solution template for .NET microservices. It ships reusable BuildingBlocks, pluggable Modules, one runnable sample service (`Orders`) that demonstrates every pattern, an API gateway, docker-compose, a dev token tool, domain tests, six Claude Code agents, and the architecture playbook.

## Repo structure

```
Starter.sln · Directory.Packages.props (all versions) · dotnet-tools.json (dotnet-ef) · docker-compose*.yml
src/
├── BuildingBlocks/
│   ├── Blocks.Exceptions           HttpException → BadRequest / NotFound / Unauthorized
│   ├── Blocks.Domain               Entity<T>, AggregateRoot<T>, value-object bases, IDomainEvent, DomainException, IAuditableAction
│   ├── Blocks.Core                 Guard, options helpers, MaxLength, cache helpers, Mapster + FluentValidation helpers, RequestContext
│   ├── Blocks.EntityFrameworkCore  ApplicationDbContext, RepositoryBase, configuration ladder, interceptors, JSON seeding
│   ├── Blocks.AspNetCore           exception/context/diagnostics middlewares, HttpContextProvider, RouteKeys, gRPC client helper
│   ├── Blocks.MediatR              ICommand/IQuery, AssignUserId → Validation → Logging behaviors, domain-event publisher
│   ├── Blocks.FastEndpoints        FastEndpoints variant (publisher, AssignUserIdPreProcessor, UseCustomFastEndpoints)
│   ├── Blocks.Messaging            AddMassTransitWithRabbitMQ, snake_case + service-suffix queue names
│   ├── Blocks.Http.Abstractions    IFormFile helpers
│   ├── Blocks.Redis                Redis.OM Entity + Repository<T>           (optional)
│   ├── Blocks.Hasura               Hasura GraphQL client + metadata bootstrap (optional, read models)
│   ├── Starter.Abstractions        shared kernel: IAggregateAction, AggregateCommandBase<T>, StageChanged<T>, UserRoleType, OrderStage
│   ├── Starter.Security            JWT validation, Role constants, RequireRoleAuthorization (role + aggregate-access layers)
│   ├── Starter.Grpc.Contracts      code-first gRPC contracts, one folder per owning service (empty; see its README)
│   └── Starter.IntegrationEvents.Contracts   integration events + flat DTOs, one folder per producer
├── Modules/
│   ├── EmailService/               Contracts + Empty (dev) / Smtp / SendGrid
│   └── FileStorage/                Contracts + MongoGridFS / AzureBlob
├── Services/Orders/                SAMPLE: Minimal APIs + MediatR + SQL Server (see its CLAUDE.md)
├── ApiGateway/                     YARP, configuration-only routes, X-Correlation-ID at the edge
└── SharedData/Master/              master-data JSON shared by several services (link into Persistence projects)
tools/Starter.DevToken/             mints development JWTs (no identity service yet)
tests/Orders.Domain.Tests/          aggregate + state-machine tests (no DB, no mocks)
docs/playbook/                      the architecture playbook (patterns, decisions, checklist, what not to copy)
.claude/agents/                     dotnet-architect, -service-scaffolder, -domain-modeler, -slice-developer, -integration-engineer, -architecture-reviewer
```

## Core principles

- **Clean Architecture decides references.** Each service is a linear stack `API → Application → Persistence → Domain`. The Domain references nothing outward. Services never reference each other; contracts cross boundaries only through `Starter.*.Contracts`.
- **Vertical Slice decides grouping.** `Features/{Area}/{Operation}/` holds command + validator (one file), handler, slice-only event handlers and mappings.
- **DDD decides where rules live.** Aggregate methods enforce invariants (`DomainException`); handlers only orchestrate: load → call one aggregate method → save.
- **CQRS via MediatR** (or FastEndpoints). Failures are thrown and translated once by `GlobalExceptionMiddleware`.

## Naming conventions

- Private fields `_camelCase`; public members/types `PascalCase`; locals/parameters descriptive `camelCase`; `ct` for CancellationToken.
- No abbreviations: never `req`, `cmd`, `res`, `ops`, `_q`, `_m`.
- `{Verb}{Noun}Command`, `{Get|Search}{Noun}Query`, `{Noun}{PastTense}` domain events, `{Effect}On{Event}Handler`, `PublishIntegrationEventOn{Event}Handler`, `{Noun}{PastTense}[For{Purpose}]Event`, `{Aggregate}Repository`, `{Service}DbContext`, `{Thing}Options` (config section = class name).

## Architecture guardrails

- No service-layer classes (`OrderService`). Use domain methods, handlers, repositories, gRPC clients, infrastructure helpers.
- No repository interfaces without a second implementation; the repository is the unit of work (`SaveChangesAsync`).
- No god folders (`Services/`, `Helpers/`, `Utils/`, `Handlers/`, `Validators/`, `Controllers/`).
- No inventing patterns. Mirror `src/Services/Orders` and the playbook.
- No bypassing domain rules via EF configurations or endpoints; rule-governed state is `private set`.
- Domain events stay inside a service; integration events (fat DTO snapshots) cross services, published only from `PublishIntegrationEventOn{Event}Handler`.
- gRPC only to hydrate missing foreign data on a write path, or for an authoritative check at a transition. Never on read paths.
- Default to a module; make a microservice only when deployment, scaling, or ownership demands it.
- Secrets never in `appsettings.json`: user-secrets locally, environment variables in containers. `appsettings.Development.json` holds local-docker values only.

## Reference → template names (differences from the reference implementation)

| Reference (Articles system) | This template |
|---|---|
| `IArticleAction`, `ArticleCommandBase<T>` | `IAggregateAction`, `AggregateCommandBase<T>`; per service `I{Aggregate}Action` (`IOrderAction`) + `{Aggregate}Command` (`OrderCommand`) |
| `ArticleId`, route `{articleId:int}` | `AggregateId`, route **`{id:int}`** (`RouteKeys.AggregateId`, read by the aggregate-access authorization) |
| `ArticleStageChanged` | `StageChanged<TStage>` |
| `ArticleStage`, `ArticleActionType` | `OrderStage` (shared kernel), `OrderActionType` (service `_Shared/Enums`) |
| `ArticleStageTransition`, `ArticleStateMachineFactory` | `StageTransition`, `OrderStateMachineFactory` |
| `IArticleAccessChecker`, `ArticleRoleRequirement`, `ArticleAccessAuthorizationHandler` | `IAggregateAccessChecker`, `AggregateRoleRequirement`, `AggregateAccessAuthorizationHandler` |
| `Articles.*` packages | `Starter.*` |
| `action.Adapt<ArticleAction>()` in the domain | `OrderAction.From(action)`: explicit, no Mapster in the Domain |

Replace the sample vocabulary (`OrderStage`, `UserRoleType` members, `Role` constants) with your own domain's.

## Commands

```bash
dotnet tool restore                                   # dotnet-ef (pinned in dotnet-tools.json)
dotnet build Starter.sln
dotnet test
docker compose up -d sqlserver rabbitmq               # infrastructure for local runs
dotnet run --project src/Services/Orders/Orders.API   # https://localhost:4451/swagger
dotnet run --project tools/Starter.DevToken -- --user 1 --roles CUSTOMER          # paste into Swagger "Authorize"
dotnet ef migrations add Name -p src/Services/{Svc}/{Svc}.Persistence -s src/Services/{Svc}/{Svc}.API
```

## Port conventions

4400–4499. Gateway 4400. Services take HTTP/HTTPS pairs (4401/4451), (4402/4452), … in creation order.

## Agents

Use them in this order: `dotnet-architect` (plan) → `dotnet-service-scaffolder` (new service) → `dotnet-domain-modeler` → `dotnet-slice-developer` → `dotnet-integration-engineer` → `dotnet-architecture-reviewer`.

## Known open gaps (decide per project)

No transactional outbox; no MassTransit retry policy; no gRPC exception→status interceptor; correlation id not forwarded to gRPC/message headers; FastEndpoints `[Authorize(Roles)]` applies the role layer only (attach `AggregateRoleRequirement` yourself).

## Working rules for Claude

- After reading a file, trust your context; re-read only if it changed.
- When compacting, preserve domain-model decisions, current file changes, and which files were read.
