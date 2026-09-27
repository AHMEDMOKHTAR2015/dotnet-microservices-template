# 10 · Implementation checklist for a new project

> Follow in order. Each phase ends with **done-when** checks. Most are greps, the same way this repo verifies its own conventions. Links point to the pattern pages with full examples.

---

## Phase 0: Decide before coding

- [ ] List bounded contexts. For each: role (write side / reference data / read model), owned data, and the foreign data it needs → [01 §1.4](01-architecture.md#14-bounded-contexts-each-service-owns-its-own-model-of-shared-concepts)
- [ ] Apply "default to module": which capabilities are modules and which are services? → [09 D1](09-decision-framework.md#d1-module-or-microservice)
- [ ] Choose **one** endpoint framework for the project (MediatR + Minimal APIs/Carter, or FastEndpoints) → [09 D3](09-decision-framework.md#d3-endpoint-framework-and-whether-to-use-mediatr)
- [ ] Choose a storage engine per role → [09 D13](09-decision-framework.md#d13-storage-engine-per-service)
- [ ] Draft the shared vocabulary: lifecycle enum with numeric ranges per service, role enum with ranges per domain
- [ ] Draw the event map: which facts cross which boundaries → [04 §4.1](04-service-communication.md#41-communication-map)
- [ ] Assign ports: one block per system, HTTP/HTTPS pairs (the repo uses 4401–4406 / 4451–4456)

## Phase 1: Solution skeleton

- [ ] `src/` with `BuildingBlocks/`, `Modules/`, `Services/`, `ApiGateway/`, `SharedData/Master/`
- [ ] `src/Directory.Packages.props` with `ManagePackageVersionsCentrally` **and** `CentralPackageTransitivePinningEnabled`. No `Version=` in any csproj, and only one props file.
- [ ] Every csproj: `net9.0` (or current), `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`
- [ ] `docker-compose.yml` (services + infrastructure) and `docker-compose.override.yml` (ports, environment)
- [ ] Root `CLAUDE.md`/README stating guardrails (naming, no god folders, no service classes, no repository interfaces, domain vs integration events)

**Done when:** `grep -rn 'Version="' --include='*.csproj' src` → nothing.

## Phase 2: BuildingBlocks

- [ ] Copy these as they are (they're system-agnostic): `Blocks.Exceptions`, `Blocks.Domain`, `Blocks.Core`, `Blocks.EntityFrameworkCore`, `Blocks.AspNetCore`, `Blocks.Messaging`
- [ ] Add `Blocks.MediatR` **or** `Blocks.FastEndpoints` to match Phase 0
- [ ] Add `Blocks.Redis` / `Blocks.Hasura` only if a service uses them
- [ ] Create the shared kernel `{System}.Abstractions`: lifecycle enum, role enum, `I{Aggregate}Action : IAuditableAction`, `{Aggregate}CommandBase<TActionType>` with `[JsonIgnore]` provenance, `DomainEvent<TAction>`, cross-service domain events (`{Aggregate}StageChanged`), `IdResponse` → [02 §2.4](02-domain-modeling.md#24-the-action-object-commands-as-audit-records)
- [ ] Create `{System}.Security`: `AddJwtAuthentication`, `Role` constants (`nameof(UserRoleType.X)`), `RequireRoleAuthorization`, resource requirement + handler, `I{Aggregate}AccessChecker` → [06 §6.7](06-cross-cutting.md#67-security-authentication-roles-two-layer-authorization)
- [ ] Create `{System}.Grpc.Contracts` (code-first, grouped by owning service) and `{System}.IntegrationEvents.Contracts` (records + `Dtos/`, grouped by producer)

**Done when:** `Blocks.Domain` and `Blocks.Exceptions` have zero `ProjectReference`s; no `Blocks.*` project references `{System}.*`.

## Phase 3: First service skeleton

- [ ] Four projects: `{Svc}.Domain`, `{Svc}.Persistence`, `{Svc}.Application` (MediatR only), `{Svc}.API`
- [ ] References as a linear stack: API → Application → Persistence → Domain → [01 §1.2](01-architecture.md#12-anatomy-of-one-service)
- [ ] Domain references only `Blocks.Domain`/`Blocks.Core`/`Blocks.Exceptions`/`{System}.Abstractions` (prefer creation-info interfaces over referencing contract packages → [02 §2.3](02-domain-modeling.md#23-factories-four-kinds))
- [ ] `GlobalUsings.cs` in each project, grouped: Third-party / Internal libraries / Domain / Application / Persistence → [08 §8.5](08-style-guide.md#85-globalusingscs-per-project)
- [ ] A `DependencyInjection` class per layer: `ConfigureApiOptions`, `AddApiServices`, `AddApplicationServices`, `AddPersistenceServices`
- [ ] `Program.cs` with `#region Add / InitData / Use` → [01 §1.6](01-architecture.md#16-the-composition-root-programcs-in-three-regions)
- [ ] Middleware order: exception → request context → diagnostics → routing → authentication → authorization → endpoints → [06 §6.4](06-cross-cutting.md#64-middleware-pipeline-order)
- [ ] API services: `AddMemoryCache`, `AddHttpContextAccessor`, Swagger, `AddJwtAuthentication`, `AddAuthorization`, `HttpContextProvider` as `IClaimsProvider` + `IRouteProvider`, scoped `RequestContext`, the resource authorization handler
- [ ] JSON: `PropertyNameCaseInsensitive = true` + `JsonStringEnumConverter`
- [ ] Options via `AddAndValidateOptions<T>` (section = class name) → [06 §6.5](06-cross-cutting.md#65-configuration-and-options)
- [ ] `launchSettings.json` ports, Dockerfile, a docker-compose entry
- [ ] Service `CLAUDE.md`: endpoint framework, database, ports, features list

**Done when:** `grep -rn "ProjectReference" src/Services --include='*Domain.csproj' | grep -iE "Persistence|Application|API"` → nothing; the service starts and shows Swagger.

## Phase 4: Persistence

- [ ] `{Svc}DbContext : ApplicationDbContext<{Svc}DbContext>` with `ApplyConfigurationsFromAssembly`, `UseEntityTypeNamesAsTables` (+ snake_case rewriter only for Postgres), and `UnTrackCacheableEntities` in `SaveChangesAsync` → [05 §5.2](05-persistence.md#52-the-dbcontext)
- [ ] DI: scoped `DbConnection`, `ISaveChangesInterceptor` (post-save by default), `AddDbContext` using the shared connection and interceptors, `TransactionProvider`, `Repository<>`, `AddDerivedTypesOf(typeof(Repository<>))` → [05 §5.3](05-persistence.md#53-registration-shared-connection-interceptors-from-di)
- [ ] Per-service `Repository<TEntity> : RepositoryBase<{Svc}DbContext, TEntity>`
- [ ] One configuration class per entity from the ladder: `AuditedEntityConfiguration<T>` for aggregates, `EntityConfiguration<T>` for entities, `EnumEntityConfiguration<T,E>`, `MetadataConfiguration<T>` → [05 §5.4](05-persistence.md#54-entity-configuration-a-base-class-ladder)
- [ ] `MaxLength.*` everywhere, enums as strings, value objects as complex properties, explicit `DeleteBehavior`
- [ ] Replicated foreign entities: `HasGeneratedId => false`
- [ ] `Data/Master/*.json` (seeded through migrations) and `Data/Test/*.json` + `Seed.SeedTestData` (Development startup), all `CopyToOutputDirectory` → [05 §5.7](05-persistence.md#57-seeding-two-channels)
- [ ] `ICacheable` reference data + `CachedRepository` + a `DatabaseCacheLoader` hosted service → [05 §5.8](05-persistence.md#58-caching-reference-data)
- [ ] First migration; `app.Migrate<{Svc}DbContext>()` in InitData

**Done when:** `grep -rn --exclude-dir=Migrations "HasMaxLength([0-9]" src/Services` → nothing; `grep -rn "interface I\w*Repository" src/Services` → nothing.

## Phase 5: Domain

- [ ] Choose the **by-aggregate** folder layout → [02 §2.11](02-domain-modeling.md#211-organizing-the-domain-project)
- [ ] `{Svc}.Domain/_Shared/`: `ArticleActionType`-style enum, service-local `IArticleAction : IArticleAction<ActionType>`, `DomainEvent` shorthand record
- [ ] Each aggregate: `{Name}.cs` (partial, restricted constructor, `required init`, `private set`, private lists exposed as `IReadOnlyList`) + `Behaviors/{Name}.cs` (factories, rule methods, `AddAction`, events) → [02 §2.2](02-domain-modeling.md#22-aggregate-shape-state-and-behavior-in-two-partial-files)
- [ ] Children created only through the parent (`internal static Create`)
- [ ] Value objects: class, private constructor + `[JsonConstructor]`, validating static factory → [02 §2.7](02-domain-modeling.md#27-value-objects)
- [ ] Metadata-bearing enums → `EnumEntity<TEnum>` + `ICacheable`
- [ ] Domain events: past-tense records carrying aggregate + action, raised only inside behavior
- [ ] Lifecycle: `IArticleStateMachine` + `ArticleStateMachineFactory` delegate + `ValidateStageTransition` in Domain; Stateless implementation in Application; `ArticleStageTransition` as a `IMetadataEntity, ICacheable` seeded from JSON; `SetStage` as the only mutation point → [02 §2.5](02-domain-modeling.md#25-lifecycle-as-data-the-table-driven-state-machine)

**Done when:** every state property on aggregates is `private set` or `init`; `grep -rn "throw new DomainException" src/Services/{Svc}` hits only Domain files.

## Phase 6: First slice

- [ ] Per-service `ArticleCommand` base (+ `ArticleCommand<TResponse>`) and `ArticleCommandValidator<T>` in `Features/_Shared/` → [03 §3.2](03-feature-slices.md#32-commands-and-queries)
- [ ] MediatR: register `AssignUserIdBehavior` → `ValidationBehavior` → `LoggingBehavior`, validators from assembly, Mapster configs, `IDomainEventPublisher` → [03 §3.4](03-feature-slices.md#34-the-mediatr-pipeline)
- [ ] Slice folder with command + validator (one file) and handler: load `…OrThrowAsync` → one aggregate call → `SaveChangesAsync` → `IdResponse` → [03 §3.3](03-feature-slices.md#33-handler-anatomy)
- [ ] Endpoint: `/api/{resources}/{articleId:int}[:verb]`, route values into the command with `with`, `RequireRoleAuthorization(...)`, `WithName`/`WithTags`/`Produces*` → [03 §3.5](03-feature-slices.md#35-three-endpoint-variants)
- [ ] `I{Aggregate}AccessChecker` implementation over local actor data, with the service's admin bypass
- [ ] Postman request in the service's folder, in lifecycle order → [07 §7.2](07-testing.md#72-how-the-manual-verification-is-organized)

**Done when:** `find src/Services -mindepth 2 -type d \( -name Controllers -o -name Validators -o -name Handlers -o -name Services -o -name Helpers \)` → nothing; `grep -rn "IsInRole" src/Services` → nothing.

## Phase 7: Events and messaging

- [ ] In-service reactions: `{Effect}On{Event}Handler` in the owning slice
- [ ] Cross-service facts: contract record + flat DTOs in `{System}.IntegrationEvents.Contracts/{Producer}/` → [04 §4.3](04-service-communication.md#43-integration-event-contracts)
- [ ] `PublishIntegrationEventOn{Event}Handler` (re-fetch full aggregate → Mapster → `IPublishEndpoint.Publish`) + `IntegrationEventsMappingConfig` in the same slice → [04 §4.4](04-service-communication.md#44-the-domain--integration-handoff)
- [ ] `AddMassTransitWithRabbitMQ(configuration, Assembly.GetExecutingAssembly())` in the layer that holds the consumers, plus a `RabbitMqOptions` section
- [ ] Consumers in business-named slice folders (`InitializeFrom{Upstream}/`), concrete persistence dependencies, an explicit idempotency variant, get-or-create for foreign data, compensation for out-of-transaction side effects → [04 §4.6](04-service-communication.md#46-consumers)
- [ ] Reference-data replicas: `{Entity}Created/UpdatedConsumer` (skip-if-exists / upsert)

**Done when:** `grep -rn "\.Publish(" src/Services --include='*.cs'` hits only `PublishIntegrationEventOn*` files; every consumer has an early idempotency check.

## Phase 8: gRPC (only where D9 says so)

- [ ] Contract interface `[ServiceContract]` + `[ProtoContract]` DTOs in `{System}.Grpc.Contracts/{Owner}/`, namespace `{Owner}.Grpc` → [04 §4.7](04-service-communication.md#47-grpc-code-first)
- [ ] Server: `AddCodeFirstGrpc(...)` + `MapGrpcService<{X}GrpcService>()`, implementation in the owner's feature folder
- [ ] Client: `GrpcServicesOptions` section (`Services:{Key}:Url`) + `AddCodeFirstGrpcClient<IService>(grpcOptions, "{Key}")`
- [ ] Use it for local-first get-or-create (store the result) or authoritative gates (throw `BadRequestException`)

**Done when:** `grep -rn "GrpcChannel.ForAddress" src/Services` → nothing (only the shared helper creates channels); read-model services register no gRPC clients.

## Phase 9: Read model (if you need a cross-service view)

- [ ] A separate service with plain entities (no aggregates), fed only by consumers → [02 §2.10](02-domain-modeling.md#210-write-model-vs-read-model)
- [ ] Engine and naming suited to querying (the repo: Postgres + snake_case + Hasura GraphQL) → [05 §5.11](05-persistence.md#511-variant-postgresql-read-model--hasura-articlehub)
- [ ] Read endpoints `.RequireAuthorization()` (authenticated only)

## Phase 10: Edge and runtime

- [ ] YARP gateway: routes from configuration, `X-Correlation-ID` added at the edge → [04 §4.10](04-service-communication.md#410-api-gateway-yarp)
- [ ] docker-compose: one container per service + infrastructure, `depends_on` with health checks where available (the repo's Postgres entry has one)

## Phase 11: Close the gaps this reference repo leaves open

Not patterns *from* the repo. These are the open items it records itself (`//todo`, the tech-debt registry, the assessment). Decide them on purpose → [11](11-what-not-to-copy.md):

- [ ] Automated tests on the seams in [07 §7.3](07-testing.md#73-test-seams-the-design-creates), starting with aggregates and state machines
- [ ] Secrets out of `appsettings.json` (user-secrets are already set up via `<UserSecretsId>`; environment variables in containers)
- [ ] Transactional outbox for integration events; consumer retry policy
- [ ] gRPC exception → status-code interceptor; client retry (the helper exists: `AddCodeFirstGrpcClientWithRetry`)
- [ ] Gateway authentication, path transforms, routes for every public service
- [ ] Resource-level authorization for attribute-routed (FastEndpoints) endpoints
- [ ] Correlation ID forwarded on gRPC calls and message headers
