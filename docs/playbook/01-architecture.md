# 01 · Architecture: topology, layers, and how the patterns fit

> **Pattern library, part 1.** For each pattern: what it does, why it's used, where it appears, and how to replicate it.

---

## 1.1 Three tiers of code: BuildingBlocks, Modules, Services

```
src/
├── BuildingBlocks/   technical primitives + shared kernel — referenced by everything
├── Modules/          in-process business capabilities — pulled into services selectively
├── Services/         deployable microservices — never reference each other
├── ApiGateway/       YARP reverse proxy
├── SharedData/       master-data JSON linked into several services
└── Directory.Packages.props   central package versions
```

| Tier | What belongs there | Rule | Evidence |
|---|---|---|---|
| **BuildingBlocks** | Technical plumbing (`Blocks.*`) and the shared domain vocabulary (`Articles.*`) | Small, single-purpose packages with minimal dependencies. No "Common" catch-all library. | `Blocks.Exceptions` and `Blocks.Domain` reference nothing. `Blocks.Core` references only `Blocks.Exceptions`. |
| **Modules** | A named business capability reused inside services (email, file storage, timeline) | In-process library, not a network hop. Pulled in only where needed. | Journals and ArticleHub reference no module. |
| **Services** | One bounded context = one deployable = one database | **No project reference to another service**, ever | No `ProjectReference` crosses services (the `docs/tech-debt/global.md` graph check found 0 edges) |

### BuildingBlocks: split by concern

| Package | Purpose | Depends on |
|---|---|---|
| [`Blocks.Exceptions`](../../src/BuildingBlocks/Blocks.Exceptions) | `HttpException` → `BadRequest`/`NotFound`/`Unauthorized` | — |
| [`Blocks.Domain`](../../src/BuildingBlocks/Blocks.Domain) | `Entity<T>`, `AggregateRoot<T>`, value-object bases, `IDomainEvent`, `DomainException`, `IAuditableAction` | MediatR.Contracts, FastEndpoints.Messaging.Core (for the event marker interfaces only) |
| [`Blocks.Core`](../../src/BuildingBlocks/Blocks.Core) | Guards, options helpers, `MaxLength`, caching helpers, Mapster helpers, FluentValidation message extensions, `IClaimsProvider`, `RequestContext` | Blocks.Exceptions |
| [`Blocks.EntityFrameworkCore`](../../src/BuildingBlocks/Blocks.EntityFrameworkCore) | `ApplicationDbContext<T>`, repository base, configuration base classes, interceptors, seeding, `TransactionProvider` | Domain, Exceptions, Core |
| [`Blocks.MediatR`](../../src/BuildingBlocks/Blocks.MediatR) | `ICommand`/`IQuery` markers, pipeline behaviors, MediatR domain-event publisher | Core, Domain |
| [`Blocks.FastEndpoints`](../../src/BuildingBlocks/Blocks.FastEndpoints) | FastEndpoints domain-event publisher, `AssignUserIdPreProcessor`, `UseCustomFastEndpoints()` | AspNetCore, Domain |
| [`Blocks.AspNetCore`](../../src/BuildingBlocks/Blocks.AspNetCore) | Middlewares, `HttpContextProvider`, `AssignUserIdFilter`, gRPC client registration | Core, Domain, Exceptions |
| [`Blocks.Http.Abstractions`](../../src/BuildingBlocks/Blocks.Http.Abstractions) | `IFormFile` helpers for Application layers that accept uploads without referencing all of ASP.NET Core | — |
| [`Blocks.Messaging`](../../src/BuildingBlocks/Blocks.Messaging) | `AddMassTransitWithRabbitMQ`, endpoint-name formatter, `RabbitMqOptions` | Core |
| [`Blocks.Redis`](../../src/BuildingBlocks/Blocks.Redis) | Redis.OM `Entity` + `Repository<T>` | Exceptions |
| [`Blocks.Hasura`](../../src/BuildingBlocks/Blocks.Hasura) | Hasura GraphQL client + metadata bootstrap | Core |
| [`Articles.Abstractions`](../../src/BuildingBlocks/Articles.Abstractions) | **Shared kernel**: `ArticleStage`, `UserRoleType`, `IArticleAction`, `ArticleCommandBase`, `ArticleStageChanged` | Domain, Core |
| [`Articles.Security`](../../src/BuildingBlocks/Articles.Security) | JWT validation, `Role` constants, `RequireRoleAuthorization`, article-access handler | Abstractions, AspNetCore |
| [`Articles.Grpc.Contracts`](../../src/BuildingBlocks/Articles.Grpc.Contracts) | Code-first gRPC service interfaces + DTOs | Abstractions |
| [`Articles.Integration.Contracts`](../../src/BuildingBlocks/Articles.Integration.Contracts) | MassTransit event records + flat DTOs (namespace `Articles.IntegrationEvents.Contracts`) | Abstractions |

**Why split so finely?** A service takes only what it uses. Journals (Redis, FastEndpoints) never pulls in EF Core or MediatR; ArticleHub never pulls in MediatR. Each technical choice stays a local decision.

**Naming rule:** `Blocks.*` = technology-agnostic plumbing you could reuse in *any* project. `Articles.*` = this system's shared language. In a new project, keep the split: `Blocks.*` stays as it is, and `Articles.*` becomes `{YourSystem}.*`.

---

## 1.2 Anatomy of one service

```
Services/Submission/
├── CLAUDE.md                 per-service facts: endpoint framework, DB, port, features
├── Submission.API/           composition root, endpoints, (FastEndpoints: slices), Dockerfile
├── Submission.Application/   Features/, Mappings/, StateMachines/, Dtos/, access checker
├── Submission.Domain/        aggregates, value objects, domain events, enums, state-machine seam
└── Submission.Persistence/   DbContext, EntityConfigurations/, Repositories/, Data/{Master,Test}
```

### The actual dependency graph: a linear stack, not an onion

This is the most important thing to understand, because it differs from textbook Clean Architecture:

```
Submission.API ──► Submission.Application ──► Submission.Persistence ──► Submission.Domain
     │                    │                          │                        │
     ▼                    ▼                          ▼                        ▼
 Blocks.AspNetCore   Blocks.MediatR            Blocks.EntityFrameworkCore   Blocks.Domain
 Articles.Security   Blocks.Messaging                                       Articles.Abstractions
 Modules (Email/     Articles.*.Contracts                                   Articles.Grpc.Contracts*
  File providers)    FileStorage.Contracts                                  FileStorage.Contracts*
```

Evidence: [`Submission.Application.csproj`](../../src/Services/Submission/Submission.Application/Submission.Application.csproj) references `Submission.Persistence`. [`Submission.API.csproj`](../../src/Services/Submission/Submission.API/Submission.API.csproj) references only `Submission.Application` among its own layers. Review and Production follow the same shape.

**In textbook Clean Architecture**, Persistence implements interfaces owned by Application (dependency inversion). **Here**, Application depends on Persistence directly and uses **concrete repository classes**. This follows from a stated guardrail in the root `CLAUDE.md`: *"No interfaces without multiple implementations for repositories."*

| What stays enforced | What's relaxed |
|---|---|
| Domain references **nothing** from Application/Persistence/API | Application → Persistence is a direct reference, not an inversion |
| No cross-service project references | Handlers can reach the `DbContext` directly for simple reads |
| Contracts cross services only through BuildingBlocks packages | |

**Why accept this?** Every repository has exactly one implementation, and the database is chosen per service and doesn't change. An `IArticleRepository` interface would add a file and an indirection and buy nothing. Invariants stay protected because they live in the Domain, which is still the innermost layer.

**How to replicate:** keep this shape. If a second implementation of a persistence seam ever appears, introduce the interface at that moment, not before.

### The Application layer is optional

| Service | Has `.Application`? | Where use-case logic lives |
|---|---|---|
| Submission, Review | ✔ full | `Application/Features/**` handlers (MediatR) |
| Production | thin (DTOs, state machines, access checker) | `API/Features/**`, inside FastEndpoints `HandleAsync` |
| Auth | minimal (`TokenFactory`) | `API/Features/**` endpoints |
| Journals, ArticleHub | ✘ | `API/Features/**` endpoints |

**Rule extracted:** the Application project exists when you have a **request-dispatch framework** (MediatR) that separates the endpoint from the handler. With FastEndpoints, the endpoint *is* the handler, so a separate project would just move files around.

---

## 1.3 How DDD, Vertical Slice, and Clean Architecture fit together

They don't compete, because they act on different axes:

```
                 ┌──────────── one vertical slice: "ApproveArticle" ────────────┐
.API             │ ApproveArticleEndpoint.cs      (route, auth gate)            │
.Application     │ Features/ApproveArticle/                                     │
                 │   ApproveArticleCommand.cs     (record + validator)          │
                 │   ApproveArticleCommandHandler.cs  (orchestration)           │
                 │   PublishIntegrationEventOnArticleApprovedHandler.cs         │
                 │   IntegrationEventsMappingConfig.cs                          │
.Domain          │ Article.Approve(...)           (the rule: shared by slices)  │
.Persistence     │ ArticleRepository              (shared by slices)            │
                 └──────────────────────────────────────────────────────────────┘
```

- **Clean Architecture** sets the direction references may point: which project may see which.
- **Vertical Slice** decides grouping *within* the API and Application layers: everything one use case needs sits in one folder. You don't create `Validators/`, `Handlers/`, or `Controllers/` folders (the root `CLAUDE.md` bans "god folders").
- **DDD** decides where the *rules* go: in aggregate methods in the Domain, which many slices share. A slice never re-implements a rule; it calls the aggregate.

**Result:** adding a feature touches one new folder plus, sometimes, one new aggregate method. Changing a business rule touches one aggregate method, and every slice gets the change.

---

## 1.4 Bounded contexts: each service owns its own model of shared concepts

"Article" exists **four times**, each shaped for its context:

| Service | `Article` is… | Distinctive members |
|---|---|---|
| Submission | Aggregate root for authoring | `AssignAuthor`, `CreateAsset`, `Submit`, `Approve`, `Reject` |
| Review | Aggregate root for peer review | `InviteReviewer`, `AssignReviewer`, `AssignEditor`, `Accept`, `FromSubmission(...)` |
| Production | Aggregate root for typesetting | `AssignTypesetter`, versioned files, `FromReview(...)` |
| ArticleHub | **Plain data bag** (read model) | public get/set only, no behavior |

The same goes for `Person` (with local subtypes `Author`, `Editor`, `Reviewer`, `Typesetter`) and `Journal` (a local copy with just the fields each service needs).

**Rule:** share the *vocabulary* (`ArticleStage`, `UserRoleType`, `IArticleAction` in `Articles.Abstractions`), never the *model*. Each service keeps a local copy of foreign data, fed by events or lazy gRPC lookups ([04](04-service-communication.md)).

### The shared kernel: `Articles.Abstractions`

```csharp
// src/BuildingBlocks/Articles.Abstractions/Enums/ArticleStage.cs
public enum ArticleStage : int
{
    None = 0,
    //Submission
    [Description("The Author created the Article")]            Created = 101,
    [Description("Author uploaded the Manuscript file")]        ManuscriptUploaded = 102,
    [Description("The Manuscript was submitted by the author")] Submitted = 103,
    // ...
    //Review
    UnderReview = 201, ReadyForDecision = 202, AwaitingRevision = 203, Rejected = 204, Accepted = 205,
    // Production
    InProduction = 300, DraftProduction = 301, FinalProduction = 302, PublicationScheduled = 304, Published = 305
}
```

Numeric ranges per service (1xx, 2xx, 3xx) keep the lifecycle ordered and readable in the database. `UserRoleType` uses the same idea: 1–9 cross-domain, 11–19 submission, 21–29 review, 31–39 production, 91–99 auth, with gaps left for future services.

Five of six Domain projects reference `Articles.Abstractions`. **Journals.Domain does not**, because journals aren't lifecycle-bearing articles. The shared kernel goes where the domain needs it, not everywhere.

---

## 1.5 Modules vs microservices

Root `CLAUDE.md`: *"Default to module. Microservice only when deployment/scaling/ownership demands it."* The reasoning is spelled out in [`IEmailService.cs`](../../src/Modules/EmailService/EmailService.Contracts/IEmailService.cs):

```csharp
//insight
// choosing between the modular monolith and microservices architecture ...
// The reason for keeping the EmailService as a modular monolith are:
// - each service needs to use a different smtp, from address or even a different service (normal smtp vs sendgrid)
// - we don't need to share anything with any other service
public interface IEmailService { Task<bool> SendEmailAsync(EmailMessage emailMessage, CancellationToken ct = default); }
```

Two module archetypes exist:

| Archetype | Shape | Example | When |
|---|---|---|---|
| **Pluggable provider** | `{Capability}.Contracts` + one project per provider; each provider references only Contracts (+ `Blocks.Core`), never another provider | `EmailService.{Contracts,Empty,Smtp,SendGrid}`, `FileService.{Contracts,MongoGridFS,AzureBlob}` + `FileStorage.MinIO` | Several implementations exist or are likely; consumers must not care which one is active |
| **Embedded domain slice** | `.Domain` + `.Persistence` + `.Application`, no Contracts, one consumer | `ArticleTimeline` (used by Production only) | A self-contained sub-domain that should share the host's database connection and transaction |

The provider is chosen **at compile time, in code**: one visible line, with the alternative commented out beside it:

```csharp
// src/Services/Submission/Submission.API/DependencyInjection.cs
services.AddMongoFileStorageAsSingletone(config);

services.AddEmptyEmailService(config);
//services.AddSmtpEmailService(config);
```

The module's own `DependencyInjection` registers its Application and Persistence parts:

```csharp
// src/Modules/ArticleTimeline/ArticleTimeline.Application/ArticleTimeline.Application/DependencyInjection.cs
public static IServiceCollection AddArticleTimeline(this IServiceCollection services, IConfiguration config)
{
    services.AddArticleTimelineApplication(config);   // MediatR handlers from this assembly + resolvers
    services.AddArticleTimelinePersistence(config);   // its own DbContext on the host's DbConnection
    return services;
}
```

---

## 1.6 The composition root: `Program.cs` in three regions

Every service's `Program.cs` reads the same way: **Add → InitData → Use**.

```csharp
// src/Services/Submission/Submission.API/Program.cs
var builder = WebApplication.CreateBuilder(args);

#region Add
builder.Services
    .ConfigureApiOptions(builder.Configuration);        // Configure Options

builder.Services
    .AddApiServices(builder.Configuration)              // API-specific services
    .AddApplicationServices(builder.Configuration)      // application-specific services
    .AddPersistenceServices(builder.Configuration);
#endregion

var app = builder.Build();

#region InitData
app.Migrate<SubmissionDbContext>();
if (app.Environment.IsDevelopment())
    app.Services.SeedTestData();
#endregion

#region Use
app
    .UseSwagger()
    .UseSwaggerUI()
    .UseRouting()
    .UseMiddleware<GlobalExceptionMiddleware>()
    .UseMiddleware<RequestContextMiddleware>()
    .UseMiddleware<RequestDiagnosticsMiddleware>()
    .UseAuthentication()
    .UseAuthorization();

app.MapAllEndpoints();
#endregion

app.Run();
```

**Rules:**
- Each layer owns a static `DependencyInjection` class with one `Add{Layer}Services(IServiceCollection, IConfiguration)` extension. `Program.cs` never registers a concrete service directly.
- Options are configured first (`ConfigureApiOptions`) so later registrations can rely on them.
- Modules get their own "Shared modules" block (see Production's `Program.cs`: `.AddArticleTimeline(...)`).
- Middleware order: see [06 §6.4](06-cross-cutting.md#64-middleware-pipeline-order).

---

## 1.7 Replicating the architecture: summary

1. Create `BuildingBlocks/` with the `Blocks.*` packages you need (start with Exceptions, Domain, Core, EntityFrameworkCore, AspNetCore). Copy them as they are; they have no knowledge of articles.
2. Create a `{System}.Abstractions` shared kernel holding only enums, action interfaces, and cross-service domain events.
3. For each bounded context create `{Svc}.Domain`, `{Svc}.Persistence`, optionally `{Svc}.Application`, and `{Svc}.API`, wired as a linear stack.
4. Put capabilities used by several services but not worth a network hop in `Modules/`, as Contracts + providers.
5. Verify the boundaries with a grep. The repo uses greps like this one as done-conditions:
   ```bash
   # Domain must not reference Application/Persistence
   grep -rn "ProjectReference" src/Services --include='*Domain.csproj' | grep -iE "Persistence|Application"   # expect: nothing
   ```
