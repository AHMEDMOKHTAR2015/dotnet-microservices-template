# 08 · Code style guide

> Naming, file organization, and C# idioms as the codebase uses them. Where the code varies, the **recommended** form is given first and the variation noted.

---

## 8.1 Projects, folders, namespaces

| Thing | Convention | Example |
|---|---|---|
| Service layer project | `{Service}.{API\|Application\|Domain\|Persistence}` | `Submission.Application` |
| Technical building block | `Blocks.{Concern}` | `Blocks.EntityFrameworkCore`, `Blocks.Messaging` |
| Shared kernel / system contracts | `{System}.{Concern}` | `Articles.Abstractions`, `Articles.Security`, `Articles.Grpc.Contracts` |
| Module | `{Capability}.Contracts` + `{Capability}.{Provider}` | `EmailService.Contracts`, `EmailService.Smtp` |
| Namespace | Mirrors the folder | `Submission.Application.Features.ApproveArticle` |
| gRPC contract namespace | `{OwningService}.Grpc` | `Auth.Grpc`, `Journals.Grpc` |
| Integration contract namespace | `{System}.IntegrationEvents.Contracts.{Producer}` | `Articles.IntegrationEvents.Contracts.Articles` |
| Slice folder | `Features/{Operation}/` or `Features/{Area}/{Operation}/` | `Features/Invitations/InviteReviewer/` |
| Shared-within-scope folder | `_Shared/` (underscore sorts it first) | `Features/_Shared/`, `UploadFiles/_Shared/`, `Domain/_Shared/` |
| Domain folders (recommended) | One per aggregate, each with `Behaviors/`, `Events/`, `ValueObjects/`, `Enums/` | `Review.Domain/Invitations/…` |
| Persistence folders | `EntityConfigurations/`, `Repositories/`, `Data/Master/`, `Data/Test/` | |
| Per-service AI/dev notes | `CLAUDE.md` at the service root: framework, DB, port, features | `Services/Review/CLAUDE.md` |

**Banned folder names** (root `CLAUDE.md`): `Services/`, `Helpers/`, `Utils/` inside a service, plus `Controllers/`, `Validators/`, `Handlers/` god-folders.

---

## 8.2 Type and file naming

| Kind | Pattern | Examples |
|---|---|---|
| Command | `{Verb}{Noun}Command` | `ApproveArticleCommand`, `InviteReviewerCommand` |
| Query | `{Get\|Search}{Noun}Query` | `GetArticleQuery`, `SearchJournalsQuery` |
| Handler | `{Command\|Query}Handler` | `ApproveArticleCommandHandler`, `GetArticleQueryHandler` |
| Validator | `{Command}Validator` | `CreateArticleCommandValidator` |
| Response | `{Operation}Response`, or `IdResponse` | `InviteReviewerResponse`, `GetArticleResponse` |
| Endpoint | `{Operation}Endpoint` | `ApproveArticleEndpoint` |
| FastEndpoints docs | `{Operation}Summary` | `AssignTypesetterSummary` |
| Domain event | `{Noun}{PastTenseVerb}` | `ArticleApproved`, `AuthorAssigned`, `ReviewerInvited`, `FileUploaded` |
| Domain-event handler | `{Effect}On{Event}Handler` | `SendConfirmationEmailOnReviewerAssignedHandler`, `NotifyProductionOfficeOnArticleAcceptedHandler` |
| Integration publisher | `PublishIntegrationEventOn{DomainEvent}Handler` | `PublishIntegrationEventOnArticleApprovedHandler` |
| Integration event | `{Noun}{PastTense}[For{Purpose}]Event` | `ArticleApprovedForReviewEvent`, `JournalCreatedEvent` |
| Consumer | `{IntegrationEvent without "Event"}Consumer` | `ArticleApprovedForReviewConsumer` |
| Consumer folder | Business meaning, not the event name | `InitializeFromSubmission/`, `InitializeFromReview/` |
| Aggregate repository | `{Aggregate}Repository : Repository<{Aggregate}>` | `ArticleRepository` |
| DbContext | `{Service}DbContext` | `SubmissionDbContext` |
| Entity configuration | `{Entity}EntityConfiguration` (`{Entity}Configuration` for metadata) | `ArticleEntityConfiguration`, `ArticleStageTransitionConfiguration` |
| Options | `{Thing}Options`, config section with the **same name** | `RabbitMqOptions`, `MongoGridFsFileStorageOptions` |
| DI entry points | `DependencyInjection` class; `Add{Layer}Services`, `ConfigureApiOptions`, `Add{Module}` | `AddPersistenceServices`, `AddArticleTimeline` |
| Delegate factory | `{Thing}Factory` delegate type | `ArticleStateMachineFactory`, `FileServiceFactory` |
| gRPC service impl | `{Contract minus I}GrpcService` | `PersonGrpcService`, `JournalGrpcService` |
| Mapster config | `{Purpose}Mappings` / `{Purpose}MappingConfig` : `IRegister` | `GrpcMappings`, `IntegrationEventsMappingConfig` |
| Enums | `{Thing}Type` (kind), `{Thing}State` (status), `{Thing}Stage` (lifecycle), `{Thing}ActionType` (audit verbs) | `AssetType`, `AssetState`, `ArticleStage`, `ArticleActionType` |
| Static category sets | Plural class of `HashSet<TEnum>` next to the enum | `ArticleStages.Submission`, `AssetTypes.FinalFiles`, `ContributionAreaCategories.MandatoryAreas` |
| Domain exception subtype | `{Rule}Exception : DomainException` | `TypesetterAlreadyAssignedException` |

**One type per file**, with one deliberate exception: a command/query file **also holds its validator** (and small response records). Aggregate state and behavior are the **same class name in two files** (`Entities/Article.cs` + `Behaviors/Article.cs`).

---

## 8.3 Members and variables

From root `CLAUDE.md`, and consistent in code:

| Kind | Convention |
|---|---|
| Private fields | `_camelCase` |
| Public members, types | `PascalCase` |
| Locals, parameters | Descriptive `camelCase`: `articleDto`, `assetTypeDefinition`, `uploadedFiles` |
| **Never abbreviate** | No `req`, `cmd`, `res`, `ops`, `_q`, `_m` |
| Primary-constructor parameters used as fields | `_camelCase` is the majority form (`ArticleRepository _articleRepository`). Plain `camelCase` also appears, especially when passed to a base constructor. Pick one per project. |
| CancellationToken | `ct` (the one accepted abbreviation, used everywhere) |

---

## 8.4 C# idioms the codebase relies on

| Idiom | Use | Example |
|---|---|---|
| **Primary constructors** | All handlers, endpoints, consumers, repositories, middlewares | `public class GetArticleQueryHandler(ArticleRepository _articleRepository) : IRequestHandler<…>` |
| **`record`** | Commands, queries, responses, DTOs, domain events, integration events | `public record ArticleApproved(Article Article, IArticleAction Action) : DomainEvent(Action);` |
| **`class`** (not record) | Entities, aggregates, **value objects** (records would override the base equality) | `public class EmailAddress : StringValueObject` |
| `with` expressions | Put route values into an immutable command | `sender.Send(command with { ArticleId = articleId })` |
| `required` + `init` | Mandatory, set-once properties | `public required string Title { get; init; }` |
| `private set` | State changed only by behavior | `public ArticleStage Stage { get; private set; }` |
| `= null!` / `= default!` | Non-nullable navigation/required members set by EF or factories | `public Journal Journal { get; init; } = null!;` |
| `partial class` | State/behavior split; also `partial` DbContexts | `public partial class Article` |
| Collection expressions | Sets and seeds | `[ContributionArea.OriginalDraft]`, `roles.Overlaps([UserRoleType.EOF, UserRoleType.REVED])` |
| Default interface methods | Behavior on a contract with no base class | `string IAuditableAction.Action => ActionType.ToString();` |
| Expression-bodied members | One-liners (queries, guards, factories) | `public bool AllowsMultipleAssets => MaxAssetCount > 1;` |
| Extension methods for cross-cutting helpers | Guards, `…OrThrowAsync`, DI registration, config | `FindByIdOrThrowAsync`, `AddAndValidateOptions<T>` |
| File-scoped namespaces | Everywhere | `namespace Submission.Domain.Entities;` |
| `Nullable` + `ImplicitUsings` enabled | Every csproj (`net9.0`) | |

---

## 8.5 `GlobalUsings.cs` per project

Each layered project has a curated `GlobalUsings.cs` (`GlobalUsing.cs` in a few), **grouped with header comments**:

```csharp
// src/Services/Submission/Submission.Application/GlobalUsing.cs
// Third-party libraries
global using MediatR;
global using Mapster;
global using FluentValidation;

// Internal libraries
global using Blocks.Core;
global using Blocks.MediatR;
global using Blocks.EntityFrameworkCore;
global using Blocks.FluentValidation;
global using Articles.Abstractions;
global using Articles.Abstractions.Enums;

// Domain
global using Submission.Domain.Entities;
// ...

// Persistence
global using Submission.Persistence.Repositories;

// aliases to resolve ambiguity or shorten long generics
global using CachedAssetRepo = Blocks.EntityFrameworkCore.CachedRepository<
        Submission.Persistence.SubmissionDbContext, Submission.Domain.Entities.AssetTypeDefinition, Articles.Abstractions.Enums.AssetType>;
```

The file carries its own trade-off note: fewer `using` lines vs "confusion about where the types are coming from" and name conflicts (e.g. `ValidationMessages`). Resolve conflicts with **global aliases** (`global using File = Submission.Domain.ValueObjects.File;`, `global using IArticleAction = Review.Domain.Shared.IArticleAction;`).

---

## 8.6 Constants and magic values

| Instead of | Use |
|---|---|
| `HasMaxLength(256)`, `MaximumLength(256)` | `MaxLength.C256` (powers of two: `C8` … `C2048`) in **both** EF configuration and validators |
| `"AUT"` in attributes | `Role.Author` (= `nameof(UserRoleType.AUT)`) |
| Inline message strings | `ValidationMessages.*` / message extensions |
| Hard-coded stage rules | Transition table JSON ([02 §2.5](02-domain-modeling.md#25-lifecycle-as-data-the-table-driven-state-machine)) |
| Hard-coded limits (max files, sizes) | `EnumEntity` reference data (`AssetTypeDefinition.MaxAssetCount`) |
| Status codes in handlers | Exception types ([06 §6.1](06-cross-cutting.md#61-error-handling)) |

---

## 8.7 Routes and HTTP

- Prefix `/api` (group or FastEndpoints `RoutePrefix`).
- Plural resources, typed route constraints: `/articles/{articleId:int}/assets/{assetId:int}`.
- Actions beyond CRUD: `:{verb}` suffix: `:submit`, `:approve`, `:reject`, `:accept`, `:decline`, `:upload`, `:download`.
- The article ID route parameter is **always** `articleId` (resource authorization depends on it).
- `WithName("…")` + `WithTags("{Area}")` + `.Produces<T>(…)` / `.ProducesProblem(…)` on every Minimal API/Carter endpoint.
- Create → `201 Created` with a location; action → `200 OK` with `IdResponse`; download → `Results.File(stream, contentType, fileName)`.

---

## 8.8 Comments: rationale markers

The codebase uses **tagged comments** to record *why*. Keep the habit, with these tags:

| Tag | Meaning | Example |
|---|---|---|
| `//insight -` | A design decision and its reason. Keep these. | `//insight - use internal factory method so that the Asset can be created only in the Domain` |
| `//talk -` | A teaching point (course-specific); in your project, turn it into an `insight` or drop it | `// talk - ways to represent collections` |
| `//todo -` | A known gap, with enough context to act on | `//todo - Implement an interceptor which will transform the exception into a valid GRPC error response` |
| Plain `//` | Explains a non-obvious line | `// delete the file if something is wrong` |

Otherwise, comments are sparse: **no XML doc comments on ordinary members**. `<summary>` appears only on base-class contracts (interceptors, `SeedFromJsonFile`, marker interfaces) and on request properties that should show up in Swagger (`UploadFileCommand.AssetType`, `.File`).

---

## 8.9 Formatting cues visible in the code

- Fluent chains in DI and EF configurations, **one call per line**, with right-aligned purpose comments in DI (`.AddMemoryCache()   // Basic Caching`).
- `#region Add` / `#region InitData` / `#region Use` in `Program.cs`, and `#region Entities` around `DbSet`s. Regions aren't used anywhere else.
- A blank line between orchestration steps in handlers (load / gate / hydrate / act / save / return).
