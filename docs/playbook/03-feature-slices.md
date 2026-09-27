# 03 · Feature slices (Vertical Slice + CQRS)

> **Pattern library, part 3.** How one use case is built from the HTTP route down to the aggregate call.

---

## 3.1 What a slice is here

A slice is **one folder per operation** containing everything that operation needs and nothing it shares:

```
Submission.Application/Features/
├── ApproveArticle/
│   ├── ApproveArticleCommand.cs                          record + validator (same file)
│   ├── ApproveArticleCommandHandler.cs                   orchestration
│   ├── PublishIntegrationEventOnArticleApprovedHandler.cs   side effect of this use case
│   └── IntegrationEventsMappingConfig.cs                 mapping only this slice needs
├── SubmitArticle/
├── UploadFiles/
│   ├── UploadManuscriptFile/  UploadSupplementaryFile/
│   └── _Shared/               generic base command + validator + handler for the family
├── JournalCreated/            JournalCreatedConsumer.cs  (a consumer is a slice too)
└── _Shared/                   ArticleCommand base, ValidationMessages
```

| Rule | Evidence |
|---|---|
| No `Controllers/`, `Validators/`, `Handlers/`, `Services/` folders anywhere | root `CLAUDE.md` "No god folders"; no such folder exists under `src/Services` |
| Group by `{Area}/{Operation}` when a service has several aggregates | Review: `Features/Articles/AcceptArticle`, `Features/Invitations/InviteReviewer`; Production: `Features/Assets/UploadFiles/UploadDraftFile` |
| Code shared by a *family* of slices goes in that family's `_Shared/` | `UploadFiles/_Shared/UploadFileCommandHandler.cs` |
| Code shared by the whole service goes in `Features/_Shared/` | `ArticleCommand`, `BaseEndpoint`, `BaseValidator`, `MappingConfig` |
| Message consumers and domain-event handlers live in the slice that owns the reaction | `Features/Articles/InitializeFromSubmission/ArticleApprovedForReviewConsumer.cs` |

**Why:** a feature changes as a unit, so it should be found and reviewed as a unit. Shared *rules* don't go in `_Shared/`; they go in the aggregate ([02](02-domain-modeling.md)). `_Shared/` holds only technical base classes and messages.

---

## 3.2 Commands and queries

### Per-service command base

Each write service derives one base record that fixes its action enum and response type:

```csharp
// src/Services/Submission/Submission.Application/Features/_Shared/ArticleCommand.cs
public abstract record ArticleCommand : ArticleCommandBase<ArticleActionType>, ICommand<IdResponse>;
public abstract record ArticleCommand<TResponse> : ArticleCommandBase<ArticleActionType>, ICommand<TResponse>;

public abstract class ArticleCommandValidator<TFileActionCommand> : AbstractValidator<TFileActionCommand>
    where TFileActionCommand : IArticleAction
{
    public ArticleCommandValidator()
        => RuleFor(c => c.ArticleId).GreaterThan(0).WithMessageForInvalidId(nameof(ArticleCommand.ArticleId));
}
```

Review and Production also add their **service-local** `IArticleAction` to the base, so a command can be passed straight into a domain method typed with that interface:

```csharp
// src/Services/Review/Review.Application/Features/Articles/_Shared/ArticleCommand.cs
public abstract record ArticleCommand : ArticleCommandBase<ArticleActionType>, IArticleAction, ICommand<IdResponse>;
```

### A command, with its validator in the same file

```csharp
// src/Services/Submission/Submission.Application/Features/CreateArticle/CreateArticleCommand.cs
public record CreateArticleCommand(int JournalId, string Title, ArticleType Type, string Scope) : ArticleCommand
{
    public override ArticleActionType ActionType => ArticleActionType.CreateArticle;
}

public class CreateArticleCommandValidator : AbstractValidator<CreateArticleCommand>
{
    public CreateArticleCommandValidator()
    {
        RuleFor(x => x.Title)
            .NotEmptyWithMessage(nameof(CreateArticleCommand.Title))
            .MaximumLengthWithMessage(MaxLength.C256, nameof(CreateArticleCommand.Title));
        RuleFor(x => x.Scope)
            .NotEmptyWithMessage(nameof(CreateArticleCommand.Scope))
            .MaximumLengthWithMessage(MaxLength.C2048, nameof(CreateArticleCommand.Scope));
        RuleFor(c => c.JournalId).GreaterThan(0).WithMessageForInvalidId(nameof(CreateArticleCommand.JournalId));
    }
}
```

Note that `MaxLength.C256` is the **same constant** the EF configuration uses for the column ([05](05-persistence.md)). Validation and schema can't drift apart.

### A command with no body fields

The action type *is* the payload; the route supplies the ID:

```csharp
public record SubmitArticleCommand : ArticleCommand
{
    public override ArticleActionType ActionType => ArticleActionType.SubmitDraft;
}
public class SubmitArticleCommandValidator : ArticleCommandValidator<SubmitArticleCommand>;
```

### Queries

```csharp
// src/Services/Submission/Submission.Application/Features/GetArticle/GetArticleQuery.cs
public record GetArticleQuery(int ArticleId) : IQuery<GetArticleResponse>;
public record GetArticleResponse(ArticleDto ArticleSummary);

public class GetArticleValidator : AbstractValidator<GetArticleQuery>
{
    public GetArticleValidator()
        => RuleFor(c => c.ArticleId).GreaterThan(0).WithMessageForInvalidId(nameof(GetArticleQuery.ArticleId));
}
```

`ICommand<T>` and `IQuery<T>` ([`Blocks.MediatR/Abstractions`](../../src/BuildingBlocks/Blocks.MediatR/Abstractions)) are thin markers over `IRequest<T>`. They document intent; **no pipeline behavior branches on them** today.

**CQRS in this codebase is logical in the write services** (same database, different code paths) and **physical for the cross-service view** (ArticleHub is a separate database fed by events; see [04](04-service-communication.md), [05](05-persistence.md)).

### Responses

- Writes return `IdResponse(int Id)` from the shared kernel, or a slice-specific record (`InviteReviewerResponse(int ArticleId, int InvitationId, string Token)`).
- Reads return `{Operation}Response` wrapping a DTO from the service's `Dtos/` folder, mapped with Mapster.

---

## 3.3 Handler anatomy

A handler **orchestrates**: load → (fetch missing foreign data) → call the aggregate → save → return. It makes no business decisions.

```csharp
// src/Services/Submission/Submission.Application/Features/ApproveArticle/ApproveArticleCommandHandler.cs
public class ApproveArticleCommandHandler(
    ArticleRepository _articleRepository, PersonRepository _personRepository,
    ArticleStateMachineFactory _stateMachineFactory, IPersonService _personClient, IJournalService _journalClient)
    : IRequestHandler<ApproveArticleCommand, IdResponse>
{
    public async Task<IdResponse> Handle(ApproveArticleCommand command, CancellationToken ct)
    {
        var article = await _articleRepository.FindByIdOrThrowAsync(command.ArticleId);         // 1. load (404 if missing)

        if (!await IsEditorAssignedToJournal(article.JournalId, command.CreatedById))          // 2. authoritative gate (gRPC)
            throw new BadRequestException($"Editor is not assigned to the article's Journal (Id: {article.JournalId})");

        var editor = await GetOrCreatePersonByUserId(command.CreatedById, command, ct);        // 3. hydrate foreign data

        article.Approve(editor, command, _stateMachineFactory);                               // 4. domain decides

        await _articleRepository.SaveChangesAsync();                                          // 5. commit (events dispatch)

        return new IdResponse(article.Id);
    }
    // private helpers: IsEditorAssignedToJournal (gRPC), GetOrCreatePersonByUserId (local → gRPC fallback)
}
```

**Conventions:**
- Primary-constructor dependencies; **concrete repositories** plus interfaces only for true abstractions (gRPC clients, `IFileService`, `IEmailService`, factory delegates).
- `...OrThrowAsync` extension methods instead of `if (x is null) throw` ([05 §5.5](05-persistence.md#55-repositories-the-repository-is-the-unit-of-work)).
- Helper methods are `private` and named for the question they answer (`IsEditorAssignedToJournal`, `GetOrCreatePersonByUserId`).
- A handler may use the `DbContext` directly for a simple lookup (`_dbContext.Authors.FindByIdOrThrowAsync(...)`). Writes go through an aggregate.

### Families of slices: template-method base handler

Upload operations differ only in which asset types they accept and which stage they move to. The family shares one generic handler, and each slice overrides a single property:

```csharp
// src/Services/Submission/Submission.Application/Features/UploadFiles/_Shared/UploadFileCommandHandler.cs
public class UploadFileCommandHandler<TUploadCommand>(
    ArticleRepository _articleRepository, CachedAssetRepo _assetTypeRepository,
    IFileService _fileService, ArticleStateMachineFactory _stateMachineFactory)
    : IRequestHandler<TUploadCommand, IdResponse> where TUploadCommand : UploadFileCommand
{
    protected Article _article = null!;

    public virtual async Task<IdResponse> Handle(TUploadCommand command, CancellationToken ct)
    {
        _article = await _articleRepository.GetByIdOrThrowAsync(command.ArticleId);
        var assetType = _assetTypeRepository.GetById(command.AssetType);
        var asset = _article.GetOrCreateAsset(assetType, command);
        _article.SetStage(NextStage, command, _stateMachineFactory);

        var uploadResponse = await UploadFile(command, asset, assetType, ct);
        try
        {
            asset.CreateFile(uploadResponse, assetType, command);
            await _articleRepository.SaveChangesAsync();
        }
        catch (Exception)
        {
            await _fileService.TryDeleteAsync(uploadResponse.StoragePath);   // compensate the out-of-transaction write
            throw;
        }
        return new IdResponse(asset.Id);
    }

    protected virtual ArticleStage NextStage => _article!.Stage;           // default: no stage change
    // ...
}

// src/Services/Submission/Submission.Application/Features/UploadFiles/UploadManuscriptFile/UploadManuscriptFileCommandHandler.cs
public class UploadManuscriptFileCommandHandler(/* same deps */)
    : UploadFileCommandHandler<UploadManuscriptFileCommand>(/* ... */)
{
    protected override ArticleStage NextStage => ArticleStage.ManuscriptUploaded;
}
```

The validator family does the same: `UploadFileValidator<T>` exposes `abstract IReadOnlyCollection<AssetType> AllowedAssetTypes`, and each slice returns its category (`AssetTypeCategories.ManuscriptAsset`, `.SupplementaryAssets`).

---

## 3.4 The MediatR pipeline

Registered identically in Submission and Review, in this order:

```csharp
// src/Services/Submission/Submission.Application/DependencyInjection.cs
services
    .AddMapsterConfigsFromAssemblyContaining<GrpcMappings>()
    .AddValidatorsFromAssemblyContaining<CreateArticleCommandValidator>()
    .AddMediatR(config =>
    {
        config.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
        config.AddOpenBehavior(typeof(AssignUserIdBehavior<,>));   // 1. who is acting
        config.AddOpenBehavior(typeof(ValidationBehavior<,>));     // 2. is the input valid
        config.AddOpenBehavior(typeof(LoggingBehavior<,>));        // 3. time the handler
    })
    .AddMassTransitWithRabbitMQ(configuration, Assembly.GetExecutingAssembly());
```

| # | Behavior | Does | Why this position |
|---|---|---|---|
| 1 | [`AssignUserIdBehavior`](../../src/BuildingBlocks/Blocks.MediatR/Behaviors/AssignUserIdBehavior.cs) | For `IAuditableAction` requests, sets `CreatedById` from the JWT (`IClaimsProvider.TryGetUserId()`) | Validators may depend on who is acting, so identity is stamped first |
| 2 | [`ValidationBehavior`](../../src/BuildingBlocks/Blocks.MediatR/Behaviors/ValidationBehavior.cs) | Runs every `IValidator<TRequest>` in parallel, throws `ValidationException` with all failures | Fail before any I/O in the handler |
| 3 | [`LoggingBehavior`](../../src/BuildingBlocks/Blocks.MediatR/Behaviors/LoggingBehavior.cs) | Begin/end debug logs with the correlation ID; `[PerfWarn]` above 1 s (3 s for file transfers) | Measures only the handler, not validation |

```csharp
public class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var context = new ValidationContext<TRequest>(request);
        var validationResults = await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, cancellationToken)));
        var failures = validationResults.Where(r => r.Errors.Any()).SelectMany(r => r.Errors).ToList();
        if (failures.Any())
            throw new ValidationException(failures);          // → 400 with per-field errors (GlobalExceptionMiddleware)
        return await next();
    }
}
```

Failures are **thrown**, never returned as a `Result<T>`. No error-monad library is referenced anywhere ([06 §6.1](06-cross-cutting.md#61-error-handling)).

---

## 3.5 Three endpoint variants

Endpoint framework is a **per-service decision** recorded in each service's `CLAUDE.md`. The slice structure stays the same; only the endpoint file differs.

### Variant A: Minimal APIs + MediatR (Submission)

A static class with one `Map` extension per endpoint, registered in one place:

```csharp
// src/Services/Submission/Submission.API/Endpoints/ApproveArticleEndpoint.cs
public static class ApproveArticleEndpoint
{
    public static void Map(this IEndpointRouteBuilder app)
    {
        app.MapPost("/articles/{articleId:int}:approve", async (int articleId, ApproveArticleCommand command, ISender sender) =>
        {
            var response = await sender.Send(command with { ArticleId = articleId });
            return Results.Ok(response);
        })
        .RequireRoleAuthorization(Role.Editor, Role.EditorAdmin)
        .WithName("ApproveArticle")
        .WithTags("Articles")
        .Produces<IdResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status401Unauthorized);
    }
}

// src/Services/Submission/Submission.API/Endpoints/XEndpointRegistration.cs
public static IEndpointRouteBuilder MapAllEndpoints(this IEndpointRouteBuilder app)
{
    var api = app.MapGroup("/api");
    GetArticleEndpoint.Map(api);
    CreateArticleEndpoint.Map(api);
    ApproveArticleEndpoint.Map(api);
    // ...
    return app;
}
```

Queries bind from the route with `[AsParameters]`:
```csharp
app.MapGet("/articles/{articleId:int}", async ([AsParameters] GetArticleQuery query, ISender sender) => Results.Ok(await sender.Send(query)))
```

Uploads bind `[FromForm]` and call `.DisableAntiforgery()` (needed for `IFormFile`).

### Variant B: Carter + MediatR (Review)

Same body; the class implements `ICarterModule` and is discovered automatically (`services.AddCarter()` + `app.MapGroup("/api").MapCarter()`). ArticleHub also uses Carter, but **without** MediatR: its read endpoints run their logic inline.

```csharp
// src/Services/Review/Review.API/Endpoints/Invitations/InviteReviewerEndpoint.cs
public class InviteReviewerEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/articles/{articleId:int}/invitations", async (int articleId, InviteReviewerCommand command, ISender sender) =>
        {
            command.ArticleId = articleId;
            var response = await sender.Send(command);
            return Results.Ok(response);
        })
        .RequireRoleAuthorization(Role.Editor, Role.EditorAdmin)
        .WithName("Invite Reviewer")
        .WithTags("Invitations")
        .Produces<IdResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status401Unauthorized);
    }
}
```

Carter removes the hand-maintained registration list of Variant A. In Review the endpoint files live in `API/Endpoints/{Area}/` and the rest of the slice in `Application/Features/{Area}/{Operation}/`.

### Variant C: FastEndpoints without MediatR (Production, Journals, Auth)

The endpoint class **is** the handler. Routing and authorization are attributes; `HandleAsync` does the orchestration. There's no Application-layer handler.

```csharp
// src/Services/Production/Production.API/Features/Articles/AssignTypesetter/AssignTypesetterEndpoint.cs
[Authorize(Roles = Role.ProdAdmin)]
[HttpPut("articles/{articleId:int}/typesetter/{typesetterId:int}")]
[Tags("Articles")]
public class AssignTypesetterEndpoint(ArticleRepository articleRepository, ArticleStateMachineFactory _stateMachineFactory, ProductionDbContext _dbContext)
    : BaseEndpoint<AssignTypesetterCommand, IdResponse>(articleRepository)
{
    public override async Task HandleAsync(AssignTypesetterCommand command, CancellationToken ct)
    {
        _article = await _articleRepository.GetByIdOrThrowAsync(command.ArticleId);
        var typesetter = await _dbContext.Typesetters.FindByIdOrThrowAsync(command.TypesetterId);

        _article.AssignTypesetter(typesetter, _stateMachineFactory, command);

        await _articleRepository.SaveChangesAsync();
        await Send.OkAsync(new IdResponse(command.ArticleId));
    }
}

// .../AssignTypesetter/AssignTypesetterSummary.cs: OpenAPI documentation as its own class
public class AssignTypesetterSummary : Summary<AssignTypesetterEndpoint>
{
    public AssignTypesetterSummary()
    {
        Summary = "Assigns a typesetter to an article";
        Description = "Assigns a typesetter to an article, transitioning it to the InProduction stage";
        Response<IdResponse>(200, "Typesetter was successfully assigned");
    }
}
```

Slice files in Variant C: `{Op}Command.cs` (record + `Validator<T>`/`BaseValidator<T>`), `{Op}Endpoint.cs`, optionally `{Op}Summary.cs`, and `{Effect}On{Event}Handler.cs` using FastEndpoints `IEventHandler<T>` (Journals, Auth) or MediatR `INotificationHandler<T>` (Production, which keeps MediatR only as its event bus).

### Equivalence table

| Concern | Minimal API + MediatR | Carter + MediatR | FastEndpoints |
|---|---|---|---|
| Route | `app.MapPost("…")` in static `Map()` | `app.MapPost("…")` in `AddRoutes()` | `[HttpPost("…")]` attribute |
| Registration | Hand-listed in `MapAllEndpoints()` | `MapCarter()` scan | `UseFastEndpoints()` scan |
| `/api` prefix | `MapGroup("/api")` | `MapGroup("/api")` | `c.Endpoints.RoutePrefix = "api"` in `UseCustomFastEndpoints()` |
| Auth | `.RequireRoleAuthorization(...)` | `.RequireRoleAuthorization(...)` | `[Authorize(Roles = …)]` |
| Handler | Separate `IRequestHandler` | Separate `IRequestHandler` | `HandleAsync` in the endpoint |
| Validator base | `AbstractValidator<T>` | `AbstractValidator<T>` | `Validator<T>` (Production: `BaseValidator<T>`) |
| Validation trigger | `ValidationBehavior` | `ValidationBehavior` | FastEndpoints built-in |
| Identity stamping | `AssignUserIdBehavior` (`AssignUserIdFilter` exists for MediatR-less Minimal APIs) | `AssignUserIdBehavior` | `AssignUserIdPreProcessor` |
| Shared endpoint code | — | — | `BaseEndpoint<TCommand,TResponse>`, `AssetBaseEndpoint<…>` |
| Docs | `.WithName/.WithTags/.Produces` | same | `[Tags]` + `Summary<TEndpoint>` |
| Domain-event handler | `INotificationHandler<T>` | `INotificationHandler<T>` | `IEventHandler<T>` (or MediatR if registered) |

How to choose one: see [09](09-decision-framework.md#d3-endpoint-framework-and-whether-to-use-mediatr).

---

## 3.6 Routes

| Rule | Example |
|---|---|
| All HTTP routes under `/api` | group prefix or FastEndpoints `RoutePrefix` |
| Resource-first, plural nouns, typed IDs | `/articles/{articleId:int}/assets/{assetId:int}` |
| Non-CRUD action → `:verb` suffix (Google-style custom method) | `/articles/{articleId:int}:submit`, `:approve`, `:reject`, `:accept`; `/assets/manuscript:upload`; `/assets/{assetId:int}:download`; `/invitations/{token}:accept` |
| **Name the article route parameter `articleId`** | The resource-authorization handler reads the route value `"articleId"` (`HttpContextProvider.GetArticleId()`). Any other name silently skips the per-article check. |
| Creates return `201` with a location | `Results.Created($"/api/articles/{response.Id}", response)` |
| Route values go into the command with `with { ... }` (or assignment) | `ArticleId` is `[JsonIgnore]`, so it can't come from the body |

---

## 3.7 Replicating a slice: summary

1. Create `Features/{Area}/{Operation}/`.
2. Add `{Operation}Command.cs`: a record deriving the service's command base, overriding `ActionType`, with its validator in the same file, using `MaxLength.*` and the message extensions.
3. Add `{Operation}CommandHandler.cs` (MediatR) or `{Operation}Endpoint.cs` (FastEndpoints): load with `…OrThrowAsync`, call **one** aggregate method, `SaveChangesAsync`, return `IdResponse`.
4. If a rule is missing, add it to the aggregate's `Behaviors/` file, not the handler.
5. Endpoint: `/api/{resources}/{articleId:int}[:verb]`, `.RequireRoleAuthorization(...)`, `.Produces*` metadata.
6. If other services must hear about it, add `PublishIntegrationEventOn{Event}Handler.cs` to the same folder ([04](04-service-communication.md)).
7. Add a request to the Postman collection under the service's folder ([07](07-testing.md)).
