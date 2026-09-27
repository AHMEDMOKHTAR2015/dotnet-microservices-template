# 04 · Service communication: events, gRPC, consumers, gateway

> **Pattern library, part 4.** How services talk to each other, how data crosses boundaries, and what happens when things fail.

---

## 4.1 Communication map

```
                      ┌──────────── gRPC (sync, read-style) ────────────┐
                      │                                                  ▼
   ┌────────────┐  IPersonService   ┌──────────┐                  ┌──────────┐
   │ Submission │ ────────────────► │   Auth   │ ◄─────────────── │ Journals │
   │            │  IJournalService  │ (Person) │  IPersonService  │ (Journal)│
   │            │ ────────────────────────────────────────────────►│          │
   └─────┬──────┘                   └──────────┘                  └────┬─────┘
         │ ArticleApprovedForReviewEvent                               │ JournalCreatedEvent
         │ (RabbitMQ fan-out)                                          │ JournalUpdatedEvent
         ├───────────────────────────────┬──────────────────┐          │ (→ Submission, Review)
         ▼                               ▼                  ▼          │
   ┌──────────┐                    ┌──────────┐       ┌────────────┐   │
   │  Review  │ ◄── IPersonService │ Journals │       │ ArticleHub │   │
   │          │     (to Auth)      │ (count++)│       │ (projection)│  │
   └────┬─────┘                    └──────────┘       └────────────┘   │
        │ ArticleAcceptedForProductionEvent                  ▲         │
        ├────────────────────────────────────────────────────┘         │
        ▼                                                              │
   ┌────────────┐                                                      │
   │ Production │  (+ ArticleTimeline module in-process)               │
   └────────────┘                                                      │
```

| Integration event | Producer (handler) | Consumers |
|---|---|---|
| `ArticleApprovedForReviewEvent` | Submission `PublishIntegrationEventOnArticleApprovedHandler` | Review (materialize article + copy files), Journals (increment `ArticlesCount`), ArticleHub (create projection) |
| `ArticleAcceptedForProductionEvent` | Review `PublishIntegrationEventOnArticleAcceptedHandler` | Production (materialize + copy files), ArticleHub (update projection) |
| `JournalCreatedEvent` / `JournalUpdatedEvent` | Journals `PublishIntegrationEventOnJournal{Created,Updated}Handler` | Submission, Review (local journal copy) |
| `PersonUpdatedEvent`, `ArticlePublishedEvent` | contracts defined, no producer yet | — |

---

## 4.2 Three ways to communicate, and when each applies

| Mechanism | Scope | Sync? | Carries | Use when | Evidence |
|---|---|---|---|---|---|
| **Domain event** | Inside one service | In-process, after `SaveChanges` | Full entities + the action | Reacting to a state change inside the same bounded context (email, timeline, *publishing an integration event*) | `ArticleApproved`, `ReviewerAssigned` |
| **Integration event** (MassTransit/RabbitMQ) | Across services | Async, pub/sub | Flat DTO snapshot | Another service must learn that something happened and keep its own copy | `ArticleApprovedForReviewEvent` |
| **gRPC** (code-first) | Across services | Sync request/response | Small contract DTOs | A write needs foreign data **now**: (a) it's missing locally, or (b) the answer must be authoritative at that moment | `IsEditorAssignedToJournalAsync`, `GetPersonByUserIdAsync` |

Root `CLAUDE.md`: *"Domain events = within service boundary. Integration events = cross-service."* A domain event **never** leaves the process, because it carries entities. Only its DTO projection does.

---

## 4.3 Integration event contracts

```csharp
// src/BuildingBlocks/Articles.Integration.Contracts/Articles/ArticleApprovedForReviewEvent.cs
namespace Articles.IntegrationEvents.Contracts.Articles;
public record ArticleApprovedForReviewEvent(ArticleDto Article);

// .../Articles/Dtos/ArticleDto.cs
public record ArticleDto(
    int Id, string Title, string Scope, string? Doi,
    ArticleType Type, ArticleStage Stage,
    JournalDto Journal, PersonDto SubmittedBy,
    DateTime SubmittedOn, DateTime? AcceptedOn, DateTime? PublishedOn,
    List<ActorDto> Actors, List<AssetDto> Assets);

public record ActorDto(UserRoleType Role, HashSet<ContributionArea> ContributionAreas, PersonDto Person);
public record AssetDto(int Id, string Name, int Number, AssetType Type, FileDto File);
public record FileDto(string OriginalName, string Name, string Extension, string FileServerId, long Size);
```

| Rule | Why |
|---|---|
| One `record` per event, wrapping one DTO | Stable, versionable shape |
| DTOs contain only primitives, shared-kernel enums, and nested DTOs | No domain type leaks across the boundary |
| **Fat events: a full snapshot**, not just an ID | Consumers (especially the read model) never need to call back ([4.8](#48-data-ownership-rules)) |
| Grouped by producing domain: `Articles/`, `Journals/`, `Persons/`, each with `Dtos/` | Easy to see who owns a contract |
| Naming: `{Subject}{PastTense}[For{Purpose}]Event` | `ArticleApprovedForReviewEvent` says both what happened and why it matters downstream |

---

## 4.4 The domain → integration handoff

The **only** place that publishes to the bus is a domain-event handler named `PublishIntegrationEventOn{DomainEvent}Handler`, in the slice that raises the event:

```csharp
// src/Services/Submission/Submission.Application/Features/ApproveArticle/PublishIntegrationEventOnArticleApprovedHandler.cs
public class PublishIntegrationEventOnArticleApprovedHandler(ArticleRepository _articleRepository, IPublishEndpoint _publishEndpoint)
    : INotificationHandler<ArticleApproved>
{
    public async Task Handle(ArticleApproved notification, CancellationToken ct)
    {
        var article = await _articleRepository.GetFullArticleByIdAsync(notification.Article.Id);   // re-fetch the full graph
        var articleDto = article.Adapt<ArticleDto>();                                              // map to the contract DTO
        await _publishEndpoint.Publish(new ArticleApprovedForReviewEvent(articleDto), ct);
    }
}
```

The mapping config sits next to it, in the same slice:

```csharp
// .../ApproveArticle/IntegrationEventsMappingConfig.cs
public class IntegrationEventsMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<ArticleActor, ActorDto>().Include<ArticleAuthor, ActorDto>();   // polymorphic actors
        config.NewConfig<Person, PersonDto>().Include<Author, PersonDto>();
        // ...
    }
}
```

**Why re-fetch instead of using `notification.Article`?** The event holds whatever the handler loaded, which may lack includes (journal, submitter). Re-fetching with `GetFullArticleByIdAsync` guarantees a complete snapshot for the fat event.

**Why a handler instead of publishing from the command handler?**
1. The aggregate decides *that* something happened, and the publish follows from that. No command handler can forget it.
2. The publish runs **after the database commit** (post-save interceptor), so a failed transaction never announces a change that didn't happen.
3. There's exactly one place to find "what does this service tell the world?": `grep "class PublishIntegrationEventOn"`.

FastEndpoints services use the same pattern with `IEventHandler<T>` (`Journals.API/Features/Journals/Create/PublishIntegrationEventOnJournalCreatedHandler.cs`).

---

## 4.5 MassTransit wiring

One shared extension, called identically by every service:

```csharp
// src/BuildingBlocks/Blocks.Messaging/MassTransit/DependencyInjection.cs
public static IServiceCollection AddMassTransitWithRabbitMQ(this IServiceCollection services, IConfiguration configuration, Assembly assembly)
{
    var rabbitMqOptions = configuration.GetSectionByTypeName<RabbitMqOptions>();
    var serviceName = assembly.GetServiceName();                       // "Review.Application" → "review"

    services.AddMassTransit(config =>
    {
        config.SetEndpointNameFormatter(new SnakeCaseWithServiceSuffixNameFormatter(serviceName));
        if (assembly != null)
            config.AddConsumers(assembly);                             // every IConsumer<T> in the assembly
        config.UsingRabbitMq((context, rabbitConfig) =>
        {
            rabbitConfig.Host(new Uri(rabbitMqOptions.Host), rabbitMqOptions.VirtualHost, h =>
            {
                h.Username(rabbitMqOptions.UserName);
                h.Password(rabbitMqOptions.Password);
            });
            rabbitConfig.ConfigureEndpoints(context);
        });
    });
    return services;
}
```

```csharp
// .../SnakeCaseWithServiceSuffixNameFormatter.cs
public override string SanitizeName(string name)
{
    name = name.Replace("EventHandler", "").Replace("Handler", "");
    name = base.SanitizeName(name);
    return $"{name}.{_serviceName}";                                   // e.g. article_approved_for_review.review
}
```

**Why the service suffix matters:** `ArticleApprovedForReviewConsumer` exists in **three** services. Without the suffix, all three would bind to the same queue and *compete* for messages, so each message would reach only one of them. The suffix gives each service its own queue, which turns one publish into a **fan-out**.

**Replicate:** call `AddMassTransitWithRabbitMQ(configuration, Assembly.GetExecutingAssembly())` from whichever layer holds the consumers (Application for MediatR services, API for the others).

---

## 4.6 Consumers

### Shape

```csharp
public class {Event}Consumer({concrete DbContext / repositories}, {interfaces for IFileService, factories})
    : IConsumer<{Event}>
{
    public async Task Consume(ConsumeContext<{Event}> context) { var dto = context.Message.{Payload}; ... }
}
```

Persistence dependencies are **concrete** (DbContext, `Repository<T>`, `ArticleRepository`), following the "no repository interfaces" rule. Non-persistence dependencies (`IFileService`, factory delegates) are abstractions.

### Three consumer archetypes

| Archetype | Example | Does |
|---|---|---|
| **Reference-data replica** | `Submission/.../JournalCreatedConsumer`, `JournalUpdatedConsumer` | Upserts a thin local copy of foreign data |
| **Stage handoff (write side)** | `Review/.../InitializeFromSubmission/ArticleApprovedForReviewConsumer`, `Production/.../InitializeFromReview/ArticleAcceptedForProductionConsumer` | Materializes a **new aggregate** through a domain factory (`Article.FromSubmission`), copies files, runs the state machine |
| **Read-model projection** | `ArticleHub/.../Consumers/*` | Maps the DTO onto plain entities with Mapster and saves |

The folder name documents the business meaning: `InitializeFromSubmission/`, `InitializeFromReview/`.

### Idempotency: three variants

MassTransit is at-least-once, so every consumer must handle duplicates. The codebase shows three strategies on purpose (the Review consumer's comment reads `//insight - inbox pattern vs simple business rules`):

| Variant | Code | Semantics | Use when |
|---|---|---|---|
| **Skip if exists** | `if (existing is not null) return;` (Journal consumers), `if (await articleRepository.ExistsAsync(id)) return;` (Production) | Duplicate is a silent no-op | Replaying is harmless and expected |
| **Throw if exists** | `await _articleRepository.EnsureNotExistsOrThrowAsync(articleDto.Id, ct);` (Review), `AnyAsync → throw BadRequestException` (ArticleHub) | Duplicate is treated as an error and faults the message | You want duplicates to be visible |
| **Upsert** | `await _journalRepository.UpsertAsync(journal);` (`JournalUpdatedConsumer`) | Last write wins | The event carries the full current state |

None of these is an inbox table. They're business-key checks, which is enough when the event creates something with a natural key (the article ID is the same in every service).

### Hydrating foreign data from the event

Consumers **get-or-create** the related Journal and Person rows from the DTO instead of calling back over gRPC:

```csharp
// src/Services/Review/Review.Application/Features/Articles/InitializeFromSubmission/ArticleApprovedForReviewConsumer.cs
private async Task<Journal> GetOrCreateJournal(ArticleDto articleDto)
{
    var journal = await _journalRepository.FindByIdAsync(articleDto.Journal.Id);
    if (journal is null)
    {
        journal = articleDto.Journal.Adapt<Journal>();
        await _journalRepository.AddAsync(journal);
    }
    return journal;
}
```

### Moving files between stages, with compensation

Each stage owns its storage technology, so the receiving consumer **copies the bytes** from the previous stage's store into its own. Every upload so far is tracked, so a failure halfway through can undo them all:

```csharp
// Review consumer (Production's is the same shape: Review GridFS → Azure Blob)
var uploadedFiles = new List<FileMetadata>();
try
{
    var actors  = await CreateActors(articleDto);
    var assets  = await CreateAssets(articleDto, uploadedFiles, context.CancellationToken);  // download from Submission store,
    var journal = await GetOrCreateJournal(articleDto);                                     // upload to Review store, track

    var action  = new ArticleAction { ArticleId = articleDto.Id, ActionType = ArticleActionType.ApproveForReview, CreatedById = /* editor */ };
    var article = Article.FromSubmission(articleDto, actors, assets, _stateMachineFactory, action);
    await _articleRepository.AddAsync(article);
    await _dbContext.SaveChangesAsync(context.CancellationToken);
}
catch (Exception)
{
    // mid-loop or post-loop failure orphans every file uploaded so far in this message;
    // compensate all of them, then rethrow so MassTransit redelivers the whole message
    foreach (var uploadedFile in uploadedFiles)
        await _reviewFileService.TryDeleteAsync(uploadedFile.StoragePath);
    throw;
}
```

The source store is chosen with a typed or factory-resolved file service ([06 §6.9](06-cross-cutting.md#69-pluggable-modules)).

---

## 4.7 gRPC, code-first

### Contract: one place, grouped by the owning service

```csharp
// src/BuildingBlocks/Articles.Grpc.Contracts/Journals/JournalContracts.cs
namespace Journals.Grpc;

[ServiceContract]
public interface IJournalService
{
    [OperationContract]
    ValueTask<GetJournalResponse> GetJournalByIdAsync(GetJournalByIdRequest request, CallContext context = default);

    [OperationContract]
    ValueTask<IsEditorAssignedToJournalResponse> IsEditorAssignedToJournalAsync(IsEditorAssignedToJournalRequest request, CallContext context = default);
}

[ProtoContract]
public class IsEditorAssignedToJournalRequest
{
    [ProtoMember(1)] public int JournalId { get; set; } = default!;
    [ProtoMember(2)] public int UserId { get; set; } = default!;
}
```

Code-first (protobuf-net.Grpc) means C# interfaces **are** the contract: no `.proto` compilation step. The `.proto` files next to them are reference documentation (compiled with `GrpcServices="None"`). Namespaces are `{OwningService}.Grpc`.

### Server

```csharp
// Journals.API/DependencyInjection.cs
services.AddCodeFirstGrpc(options =>
{
    options.ResponseCompressionLevel = CompressionLevel.Fastest;
    options.EnableDetailedErrors = true;
});
// Journals.API/Program.cs
app.MapGrpcService<JournalGrpcService>();

// Journals.API/Features/Journals/JournalGrpcService.cs: implemented inside the feature folder, like an endpoint
public class JournalGrpcService(Repository<Journal> _journalRepository) : IJournalService
{
    public async ValueTask<IsEditorAssignedToJournalResponse> IsEditorAssignedToJournalAsync(IsEditorAssignedToJournalRequest request, CallContext context = default)
    {
        var journal = await _journalRepository.GetByIdOrThrowAsync(request.JournalId);
        return new IsEditorAssignedToJournalResponse { IsAssigned = journal.ChiefEditorId == request.UserId };
    }
    // ...
}
```

### Client: one helper, configured by name

```csharp
// Submission.API/DependencyInjection.cs
var grpcOptions = config.GetSectionByTypeName<GrpcServicesOptions>();
services.AddCodeFirstGrpcClient<IPersonService>(grpcOptions, "Person");
services.AddCodeFirstGrpcClient<IJournalService>(grpcOptions, "Journal");
```

```jsonc
// appsettings.json
"GrpcServicesOptions": {
  "Retry": { "Count": 3, "InitialDelayMs": 2 },
  "Services": {
    "Person":  { "Url": "https://auth-api:8081",     "EnableRetry": true },  // docker-internal names/ports
    "Journal": { "Url": "https://journals-api:8081", "EnableRetry": true }
  }
}
```

`AddCodeFirstGrpcClient<T>` ([`Blocks.AspNetCore/Grpc/GrpcClientRegistrationExtensions.cs`](../../src/BuildingBlocks/Blocks.AspNetCore/Grpc/GrpcClientRegistrationExtensions.cs)) creates a **scoped** channel and client, accepts any certificate in `DEBUG` builds only, and fails at startup if the config key is missing. No service creates a `GrpcChannel` itself. The same file has two alternatives that nothing uses yet: `AddCodeFirstGrpcClientWithRetry` (gRPC-native retry policy that reads `EnableRetry`) and `AddCodeFirstGrpcClientAsSingleton`.

### Two legitimate uses of gRPC

**(a) Lazy hydration: local first, gRPC only when missing, then store locally.**

```csharp
// Submission ApproveArticleCommandHandler
private async Task<Person> GetOrCreatePersonByUserId(int userId, IArticleAction action, CancellationToken ct)
{
    var person = await _personRepository.GetByUserIdAsync(userId);
    if (person is null)
    {
        var response = await _personClient.GetPersonByUserIdAsync(new GetPersonByUserIdRequest { UserId = userId });
        person = Person.Create(response.PersonInfo, action);
        await _personRepository.AddAsync(person, ct);
    }
    return person;
}
```

**(b) Authoritative gate: a business or authorization check that must reflect the owner's current truth.**

```csharp
if (!await IsEditorAssignedToJournal(article.JournalId, command.CreatedById))
    throw new BadRequestException($"Editor is not assigned to the article's Journal (Id: {article.JournalId})");
```

**Not a use:** general reads, or anything the read model needs. ArticleHub registers **zero** gRPC clients.

---

## 4.8 Data ownership rules

| Rule | How |
|---|---|
| Each service owns its data and keeps **local copies** of the foreign data it needs | `Journal`, `Person` tables in Submission, Review, Production, ArticleHub |
| Descriptive foreign data is **replicated eagerly** by events | `JournalCreated/UpdatedConsumer` |
| Gaps are **filled lazily** by gRPC at the moment a write needs them | `GetOrCreate…` helpers in handlers |
| Authoritative checks go to the owner **live** over gRPC | `IsEditorAssignedToJournalAsync` |
| A read model **never** calls back; everything it needs is in the event payload | ArticleHub: fat `ArticleDto` events, no gRPC |
| IDs are **shared across services**: the article is `42` everywhere | Local copies use `HasGeneratedId => false` / `ValueGeneratedNever()` ([05](05-persistence.md)) |

---

## 4.9 Errors and resilience across boundaries

What the code does, and where it stops:

| Boundary | Behavior in code | Gap / note |
|---|---|---|
| HTTP → service | Exceptions mapped once in `GlobalExceptionMiddleware` ([06](06-cross-cutting.md#61-error-handling)) | — |
| Service → gRPC server | Server throws `NotFoundException` via `GetByIdOrThrowAsync`; `EnableDetailedErrors = true` | `//todo - Implement an interceptor which will transform the exception into a valid GRPC error response` (`JournalGrpcService`). Clients don't map `RpcException`. |
| gRPC client | Default client: **no retry**. Cancellation is passed in only some calls (`new CallOptions(cancellationToken: ct)` in `CreateAndAssignAuthorCommandHandler`; not in `ApproveArticleCommandHandler`) | The retry variant exists but isn't wired; `EnableRetry` in config only affects that variant |
| Domain event → integration publish | Runs **after commit** (post-save interceptor) | **No transactional outbox.** If the publish fails, the state change is already committed. |
| Consumer failure | Compensate side effects (file uploads) then **rethrow** | `AddMassTransitWithRabbitMQ` configures no `UseMessageRetry` or outbox, so redelivery is left to MassTransit's defaults (a faulted message goes to the `_error` queue) |
| Duplicate delivery | Business-key idempotency ([4.6](#idempotency-three-variants)) | No inbox table |
| File storage vs DB | Compensating `TryDeleteAsync` on failure | Stands in for cross-store atomicity |
| Correlation | Gateway adds `X-Correlation-ID`; services read it, log it, echo it back | Not forwarded on gRPC calls or message headers |

**For a new project:** copy the compensation and idempotency patterns as they are. Also consider the gaps: before production use, decide on an outbox, consumer retry, and gRPC status mapping. The repo leaves all three open.

---

## 4.10 API Gateway (YARP)

```csharp
// src/ApiGateway/Program.cs
//todo add authentication
builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
// ...
app.Use(async (context, next) =>
{
    const string header = "X-Correlation-ID";
    if (!context.Request.Headers.ContainsKey(header))
        context.Request.Headers[header] = Guid.NewGuid().ToString();   // start the correlation chain at the edge
    await next();
});
app.MapReverseProxy();
```

Routes are configuration-only (`/submission/{**catch-all}` → `localhost:4404`, and so on for review, production, and articlehub). The gateway is a **stub**: no authentication, no routes for Auth or Journals, and no path transforms. Its only job right now is to create the correlation ID. The pattern to keep is *"configuration-only reverse proxy + correlation ID at the edge"*. The current route table isn't ready to use.
