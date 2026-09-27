# 09 · Decision framework

> The key architectural decisions in this codebase: what was chosen, **why** (from in-code rationale comments, `CLAUDE.md` guardrails, and the code's structure), what it costs, and how to decide the same question in your project.

---

## Quick decision trees

**"I have a new capability. Where does it go?"**
```
Is it a separate business area with its own data, lifecycle, or team?
├── no  → is it needed by several services, possibly with several implementations?
│         ├── yes → Module: {Capability}.Contracts + {Capability}.{Provider}           (D1)
│         └── no  → a slice in an existing service                                   (D5)
└── yes → does it need independent deployment, scaling, or ownership?
          ├── yes → new microservice (own DB, own local copies of foreign data)      (D1, D10)
          └── no  → embedded module sharing the host's connection (ArticleTimeline)  (D1)
```

**"Something happened. Who needs to know?"**
```
Only code in this service?            → domain event + {Effect}On{Event}Handler                 (D9)
Another service?                      → PublishIntegrationEventOn{Event}Handler → fat event     (D9, D10)
I need another service's data NOW:
    it's missing locally              → gRPC get-or-create, then store it locally               (D9)
    the answer must be authoritative  → gRPC check, throw BadRequest if it fails                (D9)
    it's just descriptive             → replicate it through events instead                     (D10)
```

**"Where does this check go?"**
```
Shape of the input (required, length, format, ID > 0)   → FluentValidation in the slice            (D4)
Allowed given the current state (stage, duplicates…)    → aggregate method → DomainException       (D4, D7)
Allowed given the current user's role                   → endpoint: RequireRoleAuthorization       (D16)
Allowed given the user's relationship to this article   → IArticleAccessChecker (local data)       (D16)
Depends on another service's truth                      → handler: gRPC + BadRequestException      (D9)
```

---

## D1. Module or microservice

| | |
|---|---|
| **Decision** | Default to a **module** (in-process library). Make a microservice only when deployment, scaling, or ownership requires it. |
| **Why (from the code)** | Root `CLAUDE.md`: *"Default to module."* `IEmailService.cs` explains keeping email a module: *"each service needs to use a different smtp, from address or even a different service … we don't need to share anything with any other service."* A module would only become a service if it needed its own state shared across services (e.g. tracking every sent email). |
| **Two module archetypes** | Pluggable (Contracts + providers: Email, File) vs embedded domain slice (ArticleTimeline: Domain/Persistence/Application, one consumer, shares the host's DB connection and transaction). |
| **Cost** | A module's code is duplicated at runtime in every service that uses it, and its config has to be repeated per service. |
| **Decide** | Would two services ever need to read the *same* state this capability owns? If yes, it's a service. If each consumer needs its own instance with its own config, it's a module. |

## D2. Linear layer stack, no repository interfaces

| | |
|---|---|
| **Decision** | `API → Application → Persistence → Domain`. Application references Persistence directly and injects concrete repositories. |
| **Why** | Root `CLAUDE.md`: *"No interfaces without multiple implementations for repositories."* The storage engine is fixed per service; an interface would be one-to-one indirection. |
| **What's still protected** | The Domain references nothing outward, so invariants are independent of persistence. No cross-service references exist. |
| **Cost** | Handlers can't be unit-tested with repository mocks ([07](07-testing.md)). Swapping ORMs would touch Application code. |
| **Decide** | Keep the stack. Introduce an interface the day a *second implementation* exists, and not before. Keep the Domain free of outward references either way. |

## D3. Endpoint framework and whether to use MediatR

| | |
|---|---|
| **Decision** | Per-service choice, recorded in the service's `CLAUDE.md`: Minimal APIs + MediatR (Submission), Carter + MediatR (Review), FastEndpoints without MediatR (Production, Journals, Auth), Carter without MediatR (ArticleHub). |
| **Why** | The repo is a teaching reference showing each style, and the endpoint framework is an implementation detail of a service, not an architectural constant. Slice structure, domain, persistence, and error handling are identical across all of them. |
| **Consequence** | Choosing MediatR decides the handler shape: with MediatR you get a separate handler plus pipeline behaviors and an Application project; without it, the endpoint is the handler and there's no Application project. Production shows that the event bus is a separate choice: it uses FastEndpoints for requests and MediatR only to dispatch domain events. |
| **Decide** | Want cross-cutting pipeline behaviors (identity, validation, logging) and endpoint-free handlers you can reuse from consumers or tests? Use **MediatR** with Minimal APIs (explicit registration) or Carter (auto-discovery). Want the least code per endpoint and built-in validation/docs? Use **FastEndpoints**, and add a `BaseEndpoint<TCommand,TResponse>` for shared plumbing. **Choose one per project.** Mixing them in one system is only justified here by the teaching purpose. |

## D4. Rich aggregates; handlers only orchestrate

| | |
|---|---|
| **Decision** | Every business rule is an aggregate method that throws `DomainException`. Handlers load, call, save. |
| **Why** | Root `CLAUDE.md`: *"No service layer classes"*, *"No bypassing domain rules via EF configs or endpoints."* The `adhoc-DomainInvariantGuards` feature made `Stage`/`File`/`Status` `private set` so no handler *can* bypass a rule, and moved the asset transition guard from the endpoint into `Asset.SetState` ("single enforcement point"). |
| **Cost** | Aggregates must be loaded whole (`Query()` includes children), which is fine at this scale. |
| **Decide** | If two slices would need the same `if`, it belongs in the aggregate. |

## D5. Vertical slices over technical folders

| | |
|---|---|
| **Decision** | `Features/{Area}/{Operation}/` holds command + validator + handler + event handlers + slice-only mapping. No `Handlers/`, `Validators/`, or `Controllers/` folders. |
| **Why** | Root `CLAUDE.md` bans god folders. A feature changes as a unit, so it's reviewed as a unit. The rules it shares with other slices live in the domain, so slices can be independent without duplicating logic. |
| **Decide** | New use case → new folder. Only *technical* base classes (a command base, an upload handler base) go in `_Shared/`; shared *rules* go in the aggregate. |

## D6. Commands are audit records

| | |
|---|---|
| **Decision** | Commands derive `ArticleCommandBase<TActionType>` (`IArticleAction`). The command is passed into domain methods, recorded as an `ArticleAction` row, carried by every domain event, and used as the state-machine trigger. |
| **Why** | One object answers who/what/when/why everywhere. Provenance fields are `[JsonIgnore]` and pipeline-stamped, so clients can't spoof them. The timeline module works generically because every event has an action. |
| **Cost** | Domain methods take an interface parameter on every call. Services need a local `ArticleActionType` enum. |
| **Decide** | Adopt it whenever you need an audit trail or activity feed. It's cheap to set up at the start and expensive to add later. |

## D7. Lifecycle rules as data

| | |
|---|---|
| **Decision** | Legal `(stage, action) → stage` transitions live in an `ArticleStageTransition` table seeded from JSON, cached, and evaluated by a Stateless-backed `IArticleStateMachine` created through a factory delegate. `SetStage` is the only mutation point and always validates first. |
| **Why** | Adding or changing a transition is data, not code spread across handlers. The rule is enforced in one place. The delegate keeps the domain unaware of DI, caching, or Stateless. |
| **Cost** | Transitions aren't visible in code; you read the JSON. A missing row surfaces as a runtime `DomainException`. |
| **Decide** | Use it for any entity with more than about four states and role-dependent actions. For two or three states, a guard in the method is enough. |

## D8. Exceptions, not Result types

| | |
|---|---|
| **Decision** | Throw typed exceptions and translate them once in `GlobalExceptionMiddleware`. No `Result<T>` library. |
| **Why** | Exceptions carry their status (`HttpException(HttpStatusCode)`); the domain throws `DomainException`, which knows nothing about HTTP. Handlers stay linear with no error plumbing. |
| **Cost** | Control flow by exception. The "failure" cases don't show in method signatures. |
| **Decide** | Keep a single translation point either way. If you adopt Result types, adopt them everywhere. Mixing the two styles is worse than either one. |

## D9. Domain event vs integration event vs gRPC

| | |
|---|---|
| **Decision** | Domain events for in-service reactions; integration events (fat DTO snapshots over RabbitMQ) for cross-service facts; gRPC only for (a) lazy hydration of missing foreign data on a write path, or (b) an authoritative check at the moment of a transition. |
| **Why** | Root `CLAUDE.md`: *"Domain events = within service boundary. Integration events = cross-service."* Domain events carry entities and can't leave the process. The single domain→integration bridge (`PublishIntegrationEventOn…`) runs after commit, so a rolled-back change is never announced. `IsEditorAssignedToJournal` must reflect Journals' current truth, which a replica could get wrong. |
| **Cost** | gRPC puts the other service in the request path (no retry wired). There's no outbox, so a publish can fail after the commit. |
| **Decide** | Default to events and local replicas. Use gRPC only when stale data would give a *wrong decision*, or when the data has never been replicated. |

## D10. Fat events and local replicas

| | |
|---|---|
| **Decision** | Integration events carry a full snapshot (`ArticleDto` with journal, actors, assets). Each consumer keeps local copies of `Journal`/`Person` using the *same IDs* as the owner. |
| **Why** | Consumers, the read model especially, never call back. ArticleHub has zero gRPC clients. Shared IDs make replicas joinable and idempotency checks trivial (`ExistsAsync(articleDto.Id)`). |
| **Cost** | Bigger messages, and data duplicated in every service. The contract DTO becomes a versioning concern. |
| **Decide** | Put in the event everything any known consumer needs to act without a callback. Configure replicated entities with `HasGeneratedId => false`. |

## D11. When domain events are dispatched

| | |
|---|---|
| **Decision** | Post-save interceptor by default (Submission, Review). Transactional interceptor when handlers must write atomically with the trigger (Production + ArticleTimeline). Manual `PublishAsync` where there's no EF (Journals) or the event needs data only the endpoint has (Auth's reset-password token). |
| **Why** | After-commit dispatch is cheap and safe for side effects. The transactional variant costs a transaction per save and is worth it only when handler writes are part of the same fact. |
| **Decide** | Emails and publishing: post-save. Audit, timeline, or derived rows that must never be missing: transactional, with a scoped shared `DbConnection`. |

## D12. Consumer idempotency

| | |
|---|---|
| **Decision** | Business-key checks, no inbox table. Three variants: skip if it exists (Journals replicas, Production), throw if it exists (Review, ArticleHub), upsert (`JournalUpdated`). |
| **Why** | `//insight - inbox pattern vs simple business rules`: a natural key (the article ID, the same across services) makes duplicate detection a query. |
| **Decide** | Skip for replay-safe creates; upsert for full-state updates; throw only when you want duplicates to surface as faults. Move to an inbox when events lack natural keys or have non-idempotent side effects. |

## D13. Storage engine per service

| | |
|---|---|
| **Decision** | SQL Server + EF Core for write-side aggregates; Redis.OM for CRUD-shaped reference data with search (Journals); PostgreSQL + Hasura for the read model; GridFS or Azure Blob for files, per stage. |
| **Why** | EF Core's machinery (interceptors, repository-UoW, configuration ladder) pays off only where invariants exist. Journals gets no EF at all. Hasura gives the read model a GraphQL query surface with no code. |
| **Cost** | More infrastructure to run (docker-compose runs six infrastructure containers: SQL Server, Redis Stack, PostgreSQL, Hasura, MongoDB, RabbitMQ). Redis.OM attributes leak into Journals.Domain. |
| **Decide** | Start every write service on one relational engine with EF Core. Deviate only for a clear role mismatch: document or search data, analytics or read-heavy views, blobs. |

## D14. Enum or EnumEntity

| | |
|---|---|
| **Decision** | Plain enums for values that rarely change and carry no metadata (`AssetState`); `EnumEntity<TEnum>` tables when the value carries behavior-driving data (`AssetTypeDefinition.MaxAssetCount`, `AllowedFileExtensions`; `Stage` descriptions). |
| **Why** | `//insight - mix enums & tables together` and `//insight - keep the following properties as enums because they change quite rarely`. Code stays readable (`AssetType.Manuscript`), and limits become data. |
| **Decide** | Would an operator ever change a limit or label? Then make it an EnumEntity, seeded from `Data/Master`, marked `ICacheable`. |

## D15. Caching policy

| | |
|---|---|
| **Decision** | Cache only whole reference tables (`ICacheable`), per process, keyed by type, with no eviction, warmed at startup. |
| **Why** | A type-only key makes it impossible to cache per-request or per-entity data by accident. Reference data changes only through migrations (restart). |
| **Decide** | Cache in-process only when "changes require a deploy" is acceptable. Anything else needs a real invalidation story, which the codebase doesn't have. |

## D16. Where authorization lives

| | |
|---|---|
| **Decision** | Declared at the endpoint in two layers: role (`RequireRole`) plus resource (`ArticleRoleRequirement` → per-service `IArticleAccessChecker` over local `ArticleActors`). Never in handlers or the domain. |
| **Why** | Auditable by reading endpoint definitions. Resource checks use replicated local data, so there's no synchronous cross-service call in the request path. The admin bypass differs per service. |
| **Cost** | The resource layer depends on the route parameter being named `articleId`, and FastEndpoints services don't wire it ([11](11-what-not-to-copy.md)). |
| **Decide** | Copy the two-layer extension. For attribute-based frameworks, make sure the resource requirement is attached too. |

## D17. File storage: one store per stage, typed resolution

| | |
|---|---|
| **Decision** | Each stage owns its file store; stage-handoff consumers copy bytes into their own store. Several stores per service are resolved by **generic options marker** (`IFileService<TOptions>`), with a **factory delegate** only when the choice depends on a runtime value (Review). |
| **Why** | Service autonomy: a stage can change storage technology (Production uses Azure Blob) without touching others. The type system is the DI key (no `AddKeyed` anywhere). |
| **Cost** | Bytes are duplicated per stage, and a copy that fails halfway needs compensation (implemented). |
| **Decide** | Inject both stores directly when they're always used together; use the factory only for runtime selection. Always compensate uploads that sit outside the DB transaction. |

## D18. Database naming conventions per engine

| | |
|---|---|
| **Decision** | Table = singular CLR type name (TPH root). PascalCase on SQL Server; **snake_case only for PostgreSQL** (ArticleHub, via `SnakeCaseNameRewriter`). HTTP JSON stays camelCase everywhere. |
| **Why** | Follow each target system's own convention: Postgres/Hasura expect snake_case, SQL Server doesn't care, and the frontend expects camelCase. |
| **Decide** | Choose naming per engine, not per repository, and apply it in one place (`UseEntityTypeNamesAsTables(rewriter)`). |
