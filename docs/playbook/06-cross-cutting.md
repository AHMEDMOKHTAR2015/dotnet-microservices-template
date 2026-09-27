# 06 · Cross-cutting concerns

> **Pattern library, part 6.** Error handling, validation, logging and correlation, middleware, configuration, DI, security, mapping, and pluggable modules.

---

## 6.1 Error handling

### The model: throw, and translate in exactly one place

Failures are **exceptions**, never `Result<T>`/`OneOf`/`ErrorOr` (no such library is referenced). Each exception type describes itself, and **one middleware** turns it into an HTTP response.

```
Blocks.Exceptions (knows HTTP)            Blocks.Domain (doesn't)         third-party
HttpException(HttpStatusCode)             DomainException                 FluentValidation.ValidationException
 ├── BadRequestException   → 400           └── TypesetterAlreadyAssigned…  ArgumentException (from Guard)
 ├── NotFoundException     → 404
 └── UnauthorizedException → 401
```

```csharp
// src/BuildingBlocks/Blocks.AspNetCore/Middlewares/GlobalExceptionMiddleware.cs
public sealed class GlobalExceptionMiddleware(RequestDelegate _next, ILogger<GlobalExceptionMiddleware> _logger, IWebHostEnvironment _env)
{
    private static HttpStatusCode MapStatusCode(Exception ex) => ex switch
    {
        ValidationException   => HttpStatusCode.BadRequest,
        ArgumentException     => HttpStatusCode.BadRequest,
        BadRequestException   => HttpStatusCode.BadRequest,
        NotFoundException     => HttpStatusCode.NotFound,
        DomainException       => HttpStatusCode.BadRequest,
        UnauthorizedException => HttpStatusCode.Unauthorized,
        _                     => HttpStatusCode.InternalServerError
    };

    public async Task InvokeAsync(HttpContext context)
    {
        try { await _next(context); }
        catch (ValidationException ex)      { await HandleValidationExceptionAsync(context, ex); }   // per-field errors
        catch (OperationCanceledException)  { if (!context.Response.HasStarted) context.Response.StatusCode = 499; }  // client went away
        catch (Exception ex)                { await HandleExceptionAsync(context, ex); }
    }

    private Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var statusCode = MapStatusCode(exception);
        if (statusCode >= HttpStatusCode.InternalServerError)
            _logger.LogError(exception, "Unhandled exception. TraceId={TraceId}", context.TraceIdentifier);   // log only real faults

        var response = new
        {
            StatusCode = (int)statusCode,
            exception.Message,
            TraceId = context.TraceIdentifier,
            Details = _env.IsDevelopment() ? exception.StackTrace : null     // stack traces never leave Development
        };
        return WriteResponseAsync(context, statusCode, response);
    }
    // HandleValidationExceptionAsync → { StatusCode: 400, Message: "One or more validation errors occurred.",
    //                                    TraceId, Errors: [{ PropertyName, ErrorMessage }], Details }
}
```

FastEndpoints services validate *before* the middleware sees anything, so they shape the error through `UseCustomFastEndpoints()` into `{ statusCode, message, errors: { field: [messages] } }`.

### What to throw where

| Situation | Throw | Layer | Status |
|---|---|---|---|
| Entity not found by ID | `Guard.NotFound(x)`, `x.OrThrowNotFound()`, `…OrThrowAsync()` → `NotFoundException` | Application / Persistence extensions | 404 |
| Business rule violated | `DomainException` (or a subclass) | **Domain only** | 400 |
| Malformed primitive in a value object | `Guard.ThrowIf*` → `ArgumentException` | Domain value objects | 400 |
| Input shape invalid | FluentValidation → `ValidationException` | Slice validator (via pipeline) | 400 + field list |
| Precondition failed that depends on *another* service or on an application-level check | `BadRequestException` | Application handler | 400 |
| Duplicate on create | `BadRequestException` (`EnsureNotExistsOrThrowAsync`) | Application / consumer | 400 |
| Anything unexpected | let it escape | — | 500 + logged |

```csharp
// src/BuildingBlocks/Blocks.Core/Guard.cs  and  GuardExtensions.cs
public static T NotFound<T>(T? value) where T : class
    => value ?? throw new NotFoundException($"{typeof(T).Name} not found");

//insight - use this method as well so we can see the difference between Guard.NotFound and the extension method.
public static T OrThrowNotFound<T>(this T? value, string? message = null) where T : class
    => value ?? throw new NotFoundException(message ?? $"{typeof(T).Name} not found");
```

### Where `try/catch` is allowed

Only at real I/O boundaries: the global middleware, file-storage and SMTP adapters, the Hasura client, EF seeding, and **compensation** blocks (catch → undo the out-of-transaction side effect → rethrow). Business, domain, and endpoint code just throws.

---

## 6.2 Validation: two levels, two owners

| Level | Question | Owner | Tool |
|---|---|---|---|
| **Input** | Is this request well-formed? (required, lengths, ID > 0, email format, file size/extension) | The slice | FluentValidation class next to the command |
| **Invariant** | Is this operation allowed *given the current state*? | The aggregate | `DomainException`, state machine |

Don't move either into the other. A validator never loads an aggregate to check a business rule, and an aggregate never re-checks string lengths. The one grey area is validators that need **reference data** (allowed file extensions per asset type). They read from the **cached** repository, so there's no database round-trip:

```csharp
// src/Services/Submission/Submission.Application/Features/UploadFiles/_Shared/UploadFileCommand.cs
public override Task<ValidationResult> ValidateAsync(ValidationContext<TUploadFileCommand> context, CancellationToken cancellation = default)
{
    // we are overriding it to get the asset type definition before the validation
    _assetTypeDefinition = _assetTypeRepository.GetById(context.InstanceToValidate.AssetType);   // CachedRepository
    return base.ValidateAsync(context, cancellation);
}
```

### Message vocabulary

MediatR services use the shared extensions in [`Blocks.Core/FluentValidation`](../../src/BuildingBlocks/Blocks.Core/FluentValidation), so messages are consistent everywhere:

```csharp
RuleFor(x => x.Title).NotEmptyWithMessage(nameof(CreateArticleCommand.Title))                  // "{0} is required."
                     .MaximumLengthWithMessage(MaxLength.C256, nameof(CreateArticleCommand.Title));  // "{0} must not exceed {1} characters."
RuleFor(c => c.JournalId).GreaterThan(0).WithMessageForInvalidId(nameof(CreateArticleCommand.JournalId));  // "The {0} should be greater than zero."
```

Slice-specific messages go in the service's `Features/_Shared/ValidationMessages.cs`. (Production's FastEndpoints validators use a separate local `ValidatorsMessagesConstants`. See [11](11-what-not-to-copy.md).)

---

## 6.3 Logging, correlation, diagnostics

**`RequestContext`** is a plain scoped object, filled once per request and read through DI. Nothing downstream reaches into `HttpContext.Items`.

```csharp
// src/BuildingBlocks/Blocks.Core/Context/RequestContext.cs
public class RequestContext
{
    public string? CorrelationId { get; set; }
    public DateTime StartedOn { get; set; } = DateTime.UtcNow;
    public string? RemoteIp { get; set; }
    public bool IsUpload { get; set; } = false;
    public bool IsDownload { get; set; } = false;
    public bool IsFileTransfer => IsUpload || IsDownload;
}
```

**`RequestContextMiddleware`** resolves the correlation ID in priority order: `X-Correlation-ID` header (set by the gateway) → `Activity.Current.TraceId` → Kestrel `TraceIdentifier`. It echoes the ID in the response header and opens a **logging scope**, so every log line in the request carries it:

```csharp
using (_log.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = requestContext.CorrelationId ?? "" }))
{
    await _next(httpContext);
}
```

**`RequestDiagnosticsMiddleware`** (HTTP level) and **`LoggingBehavior`** (handler level) both:
- log `[Begin]`/`[End]` at **Debug** level with the correlation ID;
- log `[PerfWarn]` at **Warning** when a request takes more than **1000 ms**, or **3000 ms** for file transfers (`RequestContext.IsFileTransfer`);
- (diagnostics only) add `X-Elapsed-Ms` in Development.

**Log levels as used:** Debug for request flow, Warning for slow requests, Error only for 5xx in the exception middleware. Logging is structured (`{Placeholders}`, not interpolation).

---

## 6.4 Middleware pipeline order

The canonical order, with the reasons written as comments in [`Auth.API/Program.cs`](../../src/Services/Auth/Auth.API/Program.cs):

```csharp
app
    .UseSwagger()                                   // can be early
    .UseSwaggerUI()
    .UseHttpsRedirection()                          // safe early
    .UseMiddleware<GlobalExceptionMiddleware>()     // "must come early to catch errors"
    .UseMiddleware<RequestContextMiddleware>()      // correlation ID + log scope for everything after it   (Submission)
    .UseMiddleware<RequestDiagnosticsMiddleware>()  // timing                                              (Submission)
    .UseRouting()                                   // "must come before UseAuthorization"
    .UseAuthentication()                            // "must be before UseAuthorization"
    .UseAuthorization()                             // "must come after UseRouting and UseAuthentication"
    .UseCustomFastEndpoints();                      // or MapAllEndpoints() / MapGroup("/api").MapCarter()
```

> ⚠ Review and ArticleHub register `GlobalExceptionMiddleware` **after** `UseAuthorization`, and only Submission and ArticleHub register the context/diagnostics middlewares. Follow the order above. See [11](11-what-not-to-copy.md).

---

## 6.5 Configuration and options

| Rule | Mechanism |
|---|---|
| One options class per concern, **section name = class name** | `RabbitMqOptions`, `JwtOptions`, `EmailOptions`, `TransactionOptions`, `GrpcServicesOptions`, `MongoGridFsFileStorageOptions`, `HasuraOptions` … |
| **Fail fast at startup**, not on first use | `services.AddAndValidateOptions<T>(config)`: throws if the section is missing, validates `[Required]`/`[Range]` DataAnnotations, `ValidateOnStart()` |
| Need the values during registration | `config.GetSectionByTypeName<T>()`, null-guarded |
| Connection strings by name | `GetConnectionString("Database")`; modules use `GetConnectionStringOrThrow(options.ConnectionStringName)` |
| No ad-hoc `services.Configure<X>(section)` for your own options | Always through the helper |

```csharp
// src/BuildingBlocks/Blocks.Core/Configurations/ConfigurationExtensions.cs
public static IServiceCollection AddAndValidateOptions<TOptions>(this IServiceCollection services, IConfiguration configuration)
    where TOptions : class
{
    var section = configuration.GetSection(typeof(TOptions).Name);
    if (!section.Exists())
        throw new InvalidOperationException($"Configuration section '{section.Key}' is missing.");

    services.AddOptions<TOptions>().Bind(section).ValidateDataAnnotations().ValidateOnStart(); // fail fast
    return services;
}
```

Options that belong to a module (`EmailOptions`, file-storage options) are registered **by the module's own `Add…` extension**, not by the host.

`appsettings.json` layout per service: `ConnectionStrings` (`Database` + named extra stores) → one section per options class. Docker-internal hostnames (`auth-api:8081`, `articles-mq:5672`) are the defaults. Every API project already has a `<UserSecretsId>` for local secrets. See [11](11-what-not-to-copy.md) on secrets committed to `appsettings.json`.

---

## 6.6 Dependency-injection composition

| Pattern | Example | Why |
|---|---|---|
| One `DependencyInjection` static class per layer | `AddApiServices`, `AddApplicationServices`, `AddPersistenceServices`, `ConfigureApiOptions` | Each layer registers what it owns; `Program.cs` stays a table of contents |
| Assembly scanning over manual lists | MediatR handlers, FluentValidation validators, Mapster `IRegister`, MassTransit consumers, Carter modules, `AddDerivedTypesOf(typeof(Repository<>))` | Adding a slice needs no DI edit |
| **Delegate factories** for "create one for this value" | `ArticleStateMachineFactory(ArticleStage)`, `AssetStateMachineFactory(AssetState)`, `FileServiceFactory(FileStorageType)`, `VariableResolverFactory` | Domain and consumers ask for a runtime-parameterized dependency without a service locator or a factory class |
| **Generic type as the DI key** | `IFileService<SubmissionFileStorageOptions>` vs `IFileService` | Several instances of one implementation, each resolved by type. No keyed services (`AddKeyed` is used nowhere) |
| **Interface segregation on one implementation** | `HttpContextProvider` registered as `IClaimsProvider` **and** `IRouteProvider` | Inner layers depend on the narrow capability they need (`//insight - Solid Principle interface segregation`) |
| Lifetimes | DbContext, repositories, providers, state-machine factories: **scoped**; Mongo client/bucket, default email service: **singleton**; extra file stores: **scoped** | |

```csharp
// Submission.API/DependencyInjection.cs
services
    .AddScoped<IClaimsProvider, HttpContextProvider>()
    .AddScoped<IRouteProvider, HttpContextProvider>()
    .AddScoped<HttpContextProvider>();
services.AddScoped<RequestContext>();
```

---

## 6.7 Security: authentication, roles, two-layer authorization

### Authentication: JWT issued by Auth, validated everywhere

```csharp
// src/BuildingBlocks/Articles.Security/ConfigureAuthentication.cs
public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
{
    var jwtOptions = configuration.GetSectionByTypeName<JwtOptions>();
    services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(JwtOptions =>
        {
            JwtOptions.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,  ValidIssuer = jwtOptions.Issuer,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.Default.GetBytes(jwtOptions.Secret)),
                ValidateAudience = false,                     // single internal audience: issuer + signature are the boundary
                RequireExpirationTime = true,
                RoleClaimType = ClaimTypes.Role
            };
        });
    return services;
}
```

Auth's `TokenFactory` puts the user ID in both `sub` and `ClaimTypes.NameIdentifier`, plus name, email, and one `ClaimTypes.Role` claim per role. Refresh tokens are persisted on the `User` aggregate.

### Role vocabulary: one closed enum, ranges reserved per domain

```csharp
public enum UserRoleType : int
{
    EOF = 1,                    // Cross-domain: 1–9    Editorial Office Admin
    AUT = 11, CORAUT = 12,      // Submission: 11–19    Author, Corresponding Author
    REVED = 21, REV = 22,       // Review: 21–29        Review Editor, Reviewer
    POF = 31, TSOF = 32,        // Production: 31–39    Production Office Admin, Typesetter
    USERADMIN = 91              // Auth-only: 91–99
}

// src/BuildingBlocks/Articles.Security/Role.cs: string constants for attributes, derived with nameof
public static class Role
{
    public const string Author = nameof(UserRoleType.AUT);
    public const string Editor = nameof(UserRoleType.REVED);
    public const string EditorAdmin = nameof(UserRoleType.EOF);
    // ...
}
```

### Authorization: declarative at the endpoint, in two layers

```csharp
// src/BuildingBlocks/Articles.Security/Extensions.cs
public static TBuilder RequireRoleAuthorization<TBuilder>(this TBuilder builder, params string[] roles)
    where TBuilder : IEndpointConventionBuilder
    => builder.RequireAuthorization(policy =>
    {
        policy.RequireRole(roles);                                  // layer 1: does the user have the role at all?
        policy.Requirements.Add(new ArticleRoleRequirement(roles)); // layer 2: does the user have it *on this article*?
    });
```

Layer 2 is handled by `ArticleAccessAuthorizationHandler`. It reads `articleId` from the route and asks the **service's own** `IArticleAccessChecker`:

```csharp
// src/Services/Submission/Submission.Application/ArticleAccessChecker.cs
public class ArticleAccessChecker(SubmissionDbContext _dbContext) : IArticleAccessChecker
{
    public async Task<bool> HasAccessAsync(int? articleId, int? userId, IReadOnlySet<UserRoleType> roles, CancellationToken ct = default)
    {
        if (articleId is null) return true;                          // endpoint isn't article-specific
        if (userId is null || roles.IsNullOrEmpty()) return false;
        if (roles.Overlaps([UserRoleType.EOF, UserRoleType.REVED])) return true;   // service-specific admin bypass
        return await _dbContext.ArticleActors.AsNoTracking()
            .AnyAsync(e => e.ArticleId == articleId && e.Person.UserId == userId, ct);   // answered from LOCAL data
    }
}
```

| Rule | Why |
|---|---|
| Authorization is **declared on the endpoint**, never checked inside handlers or the domain (`IsInRole` appears nowhere in business code) | You can audit it by reading endpoint definitions |
| Resource access is answered from **local, event-replicated** `ArticleActors` | No synchronous cross-service call in the request's hot path |
| Admin bypass is **per service** (Submission: EOF/REVED; Review: EOF; Production: POF) | Each context decides who is an admin for its stage |
| Read-only aggregate views (ArticleHub) use `.RequireAuthorization()`: authenticated only | A read model has no per-article ownership data |
| Public actions opt out explicitly | `AcceptInvitationEndpoint`: `.AllowAnonymous()` (the invitation token is the credential) |

> ⚠ The FastEndpoints services use `[Authorize(Roles = …)]`, which is **layer 1 only**. Production registers the article-access handler, but no endpoint attaches `ArticleRoleRequirement`, so its per-article check never runs. See [11](11-what-not-to-copy.md).

### Identity stamping: who is acting

The acting user is **never** taken from the request body (`CreatedById` is `[JsonIgnore]`). It's stamped from the JWT at the framework boundary, with one mechanism per endpoint framework, all using the same `IClaimsProvider`:

| Framework | Mechanism | Missing claim |
|---|---|---|
| MediatR | `AssignUserIdBehavior<,>` (pipeline, first) | skipped (`TryGetUserId`) |
| Minimal APIs without MediatR | `AssignUserIdFilter` (endpoint filter on the group) | skipped |
| FastEndpoints | `AssignUserIdPreProcessor` (global pre-processor) | throws (`GetUserId`) |

---

## 6.8 Object mapping (Mapster)

| Rule | Example |
|---|---|
| Configs implement `IRegister` and are found by assembly scan | `services.AddMapsterConfigsFromAssemblyContaining<GrpcMappings>()` or `.AddMapsterConfigsFromCurrentAssembly()` |
| Config lives with its purpose: `Mappings/GrpcMappings.cs`, `Mappings/RestEndpointMappings.cs`, or **inside the slice** (`ApproveArticle/IntegrationEventsMappingConfig.cs`) | |
| Map through value-object factories, never around them | `config.ForType<string, EmailAddress>().MapWith(src => EmailAddress.Create(src));` |
| Polymorphic children | `config.NewConfig<ArticleActor, ActorDto>().Include<ArticleAuthor, ActorDto>();` |
| Map, then set a few extra fields | `articleDto.AdaptWith<Article>(a => { a.Journal = journal; a.SubmittedById = articleDto.SubmittedBy.Id; })` |
| Commands → audit rows | `action.Adapt<ArticleAction>()` with `.Map(dest => dest.TypeId, src => src.ActionType)` |
| Where mapping happens | In handlers, publish handlers, consumers, and gRPC services. The only domain use is `action.Adapt<ArticleAction>()`. |

---

## 6.9 Pluggable modules

### EmailService: pick one provider at compile time

```csharp
// Contracts
public interface IEmailService { Task<bool> SendEmailAsync(EmailMessage emailMessage, CancellationToken ct = default); }

// Each provider exposes one registration extension that also binds EmailOptions
public static IServiceCollection AddEmptyEmailService(this IServiceCollection services, IConfiguration config)
{
    services.AddAndValidateOptions<EmailOptions>(config);
    services.AddSingleton<IEmailService, EmptyEmailService>();     // dev: logs "Skipped sending email to: …"
    return services;
}
// AddSmtpEmailService / SendGrid follow the same shape
```

Emails are sent from **domain-event handlers** named for the effect (`SendConfirmationEmailOnReviewerAssignedHandler`, `NotifyProductionOfficeOnArticleAcceptedHandler`, `SendConfirmationEmailOnUserCreatedHandler`). The one exception is `InviteReviewerCommandHandler`, which sends inline even though the aggregate raises `ReviewerInvited`. Prefer the handler form ([11](11-what-not-to-copy.md)).

### FileService: several stores in one service through a generic marker

```csharp
// src/Modules/FileService/FileService.Contracts/IFileService.cs
// we need this version of the interface so we can include multiple file storages into a single microservice.
public interface IFileService<TFileStorageOptions> : IFileService where TFileStorageOptions : IFileStorageOptions;

public interface IFileService
{
    Task<FileMetadata> UploadAsync(string storagePath, IFormFile file, bool overwrite = false, Dictionary<string, string>? tags = null, CancellationToken ct = default);
    Task<FileMetadata> UploadAsync(FileUploadRequest request, Stream stream, bool overwrite = false, Dictionary<string, string>? tags = null, CancellationToken ct = default);
    Task<(Stream FileStream, FileMetadata FileMetadata)> DownloadAsync(string fileId, CancellationToken ct = default);
    Task<bool> TryDeleteAsync(string fileId, CancellationToken ct = default);
    // ... by-tag variants
}
public record FileUploadRequest(string StoragePath, string FileName, string ContentType, long FileSize = default);
public record FileMetadata(string StoragePath, string FileName, string ContentType, long FileSize, string FileId);
```

- **Default store:** `AddMongoFileStorageAsSingletone(config)` → `IFileService` (singleton).
- **Extra store:** subclass the options (`public class SubmissionFileStorageOptions : MongoGridFsFileStorageOptions;`), which gives it a separate config section, then `AddMongoFileStorageAsScoped<SubmissionFileStorageOptions>(config)` → `IFileService<SubmissionFileStorageOptions>`.
- **Resolve:**
  - **Inject both directly** when a consumer always needs both stores (Production: `IFileService<ReviewFileStorageOptions> reviewFileService, IFileService azureBlobFileService`).
  - **Use a factory delegate** when the choice depends on a runtime value (Review):
    ```csharp
    public delegate IFileService FileServiceFactory(FileStorageType fileStorageType);
    services.AddScoped<FileServiceFactory>(sp => type => type switch
    {
        FileStorageType.Submission => sp.GetRequiredService<IFileService<SubmissionFileStorageOptions>>(),
        FileStorageType.Review     => sp.GetRequiredService<IFileService<MongoGridFsFileStorageOptions>>(),
        _ => throw new ApplicationException()
    });
    ```
- Provider-native types (`BsonDocument`, `BlobHttpHeaders`) **never** cross the `IFileService` boundary. Only `FileUploadRequest`/`FileMetadata` do.
- Storage paths come from the domain (`asset.GenerateStorageFilePath(fileName)` → `Articles/{ArticleId}/{AssetName}/{fileName}`), and uploads are tagged `{ entity, entityId }` so files can be found by owner.

### ArticleTimeline: an embedded module reacting generically to domain events

```csharp
// src/Modules/ArticleTimeline/.../EventHandlers/AddTimelineEventHandler.cs
public abstract class AddTimelineEventHandler<TDomainEvent, TAction>(TransactionProvider _transactionProvider, TimelineRepository _timelineRepository,
                                                                     DbContext _dbContext, VariableResolverFactory _variableResolverFactory)
    : INotificationHandler<TDomainEvent>
    where TDomainEvent : DomainEvent<TAction> where TAction : IArticleAction
{
    public async Task Handle(TDomainEvent eventModel, CancellationToken ct)
    {
        _dbContext.Database.UseTransaction(await _transactionProvider.GetCurrentTransaction(ct));  // join the host's transaction
        var template = await _timelineRepository.GetTimelineTemplate(GetSourceType(), GetSourceId(eventModel));
        // resolve <<Variables>> in the template, add a Timeline row, save
    }
    protected abstract SourceType GetSourceType();
    protected abstract string GetSourceId(TDomainEvent eventModel);
}

// In the module (generic):
public class AddTimelineWhenArticleStageChangedEventHandler(...) : AddTimelineEventHandler<ArticleStageChanged, IArticleAction>(...)
{
    protected override SourceType GetSourceType() => SourceType.StageTransition;
    protected override string GetSourceId(ArticleStageChanged e) => $"{e.CurrentStage}->{e.NewStage}";
}
// In the host (service-specific event): Production.API/Features/Articles/Timeline/AddTimelineWhenAssetActionExecutedHandler.cs
```

Timeline text is **data** (a `TimelineTemplate` per source) with `<<Variable>>` placeholders, filled in by `IVariableResolver`s. Adding a new timeline entry means adding a template row plus, at most, a three-line handler subclass.

---

## 6.10 JSON

Every service sets only `PropertyNameCaseInsensitive = true` plus `JsonStringEnumConverter`. It keeps ASP.NET's default **camelCase** on the wire, and **enums travel as strings**:

```csharp
.Configure<JsonOptions>(opt =>
{
    opt.SerializerOptions.PropertyNameCaseInsensitive = true;
    opt.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
```

Only `Blocks.Hasura` sets explicit naming policies, because it talks to Hasura and Postgres (camelCase GraphQL, snake_case metadata API).
