# 02 · Domain modeling (DDD tactical patterns)

> **Pattern library, part 2.** Where business rules live and how they're shaped.

**Core principle:** *handlers orchestrate, aggregates decide.* Every rule ("an author can't be assigned twice", "you can only submit from ManuscriptUploaded", "a manuscript needs the mandatory contribution areas") lives in a domain method. Handlers load, call, and save.

---

## 2.1 The base types (`Blocks.Domain`)

| Type | Purpose | Key detail |
|---|---|---|
| `Entity<TKey>` / `Entity` (int) | Identity + equality | Equal when same type family and same `Id`; two *new* entities (`Id == default`) are **never** equal |
| `AggregateRoot<TKey>` / `AggregateRoot` | Consistency boundary | Adds audit fields + a private domain-event list |
| `IAuditedEntity<TKey>` | `CreatedById`, `CreatedOn`, `LastModifiedById`, `LastModifiedOn` | Only aggregates carry audit columns |
| `StringValueObject`, `SingleValueObject<T>`, `ValueObject` | Value semantics | Equality by value; `ValueObject` uses `GetEqualityComponents()` |
| `EnumEntity<TEnum>` | An enum that's also a table row | `Id` **is** the enum value; adds `Name` + `Description` |
| `IAssociationEntity` | Link entity owned by an aggregate (two IDs + extra fields) | No repository of its own |
| `IMetadataEntity` | Configuration rows (e.g. transition tables) | Mapped with `MetadataConfiguration<T>` |
| `IDomainEvent` | Event marker | `: INotification, IEvent`, so it works with **both** MediatR and FastEndpoints |
| `DomainException` | Business-rule violation | Plain `Exception`, knows nothing about HTTP |

```csharp
// src/BuildingBlocks/Blocks.Domain/Entities/AggregateRoot.cs
public abstract class AggregateRoot<TPrimaryKey> : Entity<TPrimaryKey>, IAggregateRoot<TPrimaryKey>
    where TPrimaryKey : struct
{
    //insight - audited properties are required only in the aggregates because when we are saving the other
    // entities they are going to be part of an aggregate therefore they are going to inherit the same audited values
    public TPrimaryKey CreatedById { get; init; }
    public DateTime CreatedOn { get; init; } = DateTime.UtcNow;
    public TPrimaryKey? LastModifiedById { get; set; }
    public DateTime? LastModifiedOn { get; set; }

    private List<IDomainEvent> _domainEvents = new();
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;
    public void AddDomainEvent(IDomainEvent eventItem) => _domainEvents.Add(eventItem);
    public void ClearDomainEvents() => _domainEvents.Clear();
}
```

**Why audit only aggregates?** Child entities are always saved *through* their aggregate, so the aggregate's audit stamp covers the whole unit. Fewer columns, one source of truth.

**Why events on the aggregate?** Domain events are a side channel the aggregate *owns*: outside code can read them but can't inject them. They're collected during the use case and dispatched after save ([05 §5.6](05-persistence.md#56-domain-event-dispatch-savechanges-interceptors)).

**If you can't inherit** (Auth's `User` already extends `IdentityUser<int>`), implement `IAggregateRoot` by hand. [`Auth.Domain/Users/User.cs`](../../src/Services/Auth/Auth.Domain/Users/User.cs) copies the audit fields and the event list.

---

## 2.2 Aggregate shape: state and behavior in two partial files

Each aggregate is a `partial class` split into:
- a **state file**: properties, collections, nothing that decides anything;
- a **behavior file** (in a `Behaviors/` folder): factories and methods that enforce rules and raise events.

**State:** [`Submission.Domain/Entities/Article.cs`](../../src/Services/Submission/Submission.Domain/Entities/Article.cs)

```csharp
public partial class Article : AggregateRoot
{
    internal Article() {}                                  // outsiders must use the factory

    public required string Title { get; init; }            // set once at creation
    public ArticleType Type { get; init; }
    public string Scope { get; init; } = default!;

    public DateTime? SubmittedOn { get; set; }
    public int? SubmittedById { get; set; }

    public ArticleStage Stage { get; private set; }        // only behavior may change it

    public int JournalId { get; init; }
    public required Journal Journal { get; init; } = null!;

    private readonly List<Asset> _assets = new();          // private list…
    public IReadOnlyList<Asset> Assets => _assets.AsReadOnly();   // …read-only view

    private readonly List<ArticleActor> _actors = new();
    public IReadOnlyList<ArticleActor> Actors => _actors.AsReadOnly();

    private readonly List<StageHistory> _stageHistories = new();
    public IReadOnlyList<StageHistory> StageHistories => _stageHistories.AsReadOnly();

    private readonly List<ArticleAction> _actions = new();
    public IReadOnlyList<ArticleAction> Actions => _actions.AsReadOnly();
}
```

**Behavior:** [`Submission.Domain/Behaviors/Article.cs`](../../src/Services/Submission/Submission.Domain/Behaviors/Article.cs)

```csharp
public partial class Article
{
    public static Article Create(string title, ArticleType type, string scope, Journal journal, IArticleAction action)
    {
        var article = new Article
        {
            Title = title, Type = type, Scope = scope, Journal = journal,
            Stage = ArticleStage.Created,
            CreatedById = action.CreatedById,
            CreatedOn = action.CreatedOn
        };
        article.AddDomainEvent(new ArticleCreated(article, action));
        return article;
    }

    public void AssignAuthor(Author author, HashSet<ContributionArea> contributionAreas,
                             bool isCorrespondingAuthor, IArticleAction<ArticleActionType> action)
    {
        var role = isCorrespondingAuthor ? UserRoleType.CORAUT : UserRoleType.AUT;

        if (_actors.Exists(a => a.PersonId == author.Id && a.Role == role))
            throw new DomainException($"Author {author.Email} is already assigned to the article");

        _actors.Add(new ArticleAuthor { ContributionAreas = contributionAreas, Person = author, Role = role });

        AddDomainEvent(new AuthorAssigned(author, action));
        AddAction(action);
    }

    public void Submit(IArticleAction<ArticleActionType> action, ArticleStateMachineFactory _stateMachineFactory)
    {
        var contributionAreas = _actors.OfType<ArticleAuthor>()
            .SelectMany(author => author.ContributionAreas).ToHashSet();

        var missingMandatoryAreas = ContributionAreaCategories.MandatoryAreas.Except(contributionAreas).ToList();
        if (missingMandatoryAreas.Count > 0)
            throw new DomainException($"Cannot submit article: Missing mandatory contribution areas: {string.Join(", ", missingMandatoryAreas)}");

        SubmittedById = action.CreatedById;
        SubmittedOn = action.CreatedOn;
        SetStage(ArticleStage.Submitted, action, _stateMachineFactory);
    }

    private void AddAction(IArticleAction<ArticleActionType> action)
    {
        _actions.Add(action.Adapt<ArticleAction>());                  // the command becomes an audit row
        AddDomainEvent(new ArticleActionExecuted(this, action));
    }
}
```

**Why split the files?** "What an aggregate *has*" and "what it *does*" change for different reasons. The state file is what EF Core configurations and DTO mappings care about. The behavior file is what reviewers of business rules care about. A reviewer can read every rule of an aggregate in one file, without scrolling past property declarations.

### Encapsulation checklist (as the code does it)

| Technique | Purpose | Example |
|---|---|---|
| `internal`/`private` constructor | Force creation through a factory | `internal Article() {}`, `private Asset() {/* use factory method*/}` |
| `required` + `init` | Mandatory at creation, immutable afterwards | `public required string Title { get; init; }` |
| `private set` | Only behavior may change it | `Stage`, `ReviewInvitation.Status`, `Asset.File` |
| `private readonly List<T>` + `IReadOnlyList<T>` | Children added only through behavior | `_assets`, `_actors`, `_actions` |
| Child created by parent | Invariants that span siblings (e.g. max count) stay enforceable | `Article.CreateAsset` → `internal static Asset.Create(...)` |

The `adhoc-DomainInvariantGuards` feature ([`docs/specs/.../summary.md`](../specs/adhoc-DomainInvariantGuards/delivery/summary.md)) tightened exactly these: `Stage` and `File` became `private set` in all three write services so no handler can bypass a transition.

---

## 2.3 Factories: four kinds

| Kind | Signature pattern | Where | Why |
|---|---|---|---|
| **Aggregate factory** | `static Article Create(..., IArticleAction action)` | Submission `Article.Create` | Sets the initial stage, audit, and creation event in one place |
| **Create through the parent** | `journal.CreateArticle(...)`, `article.CreateAsset(type, action)` | Submission `Journal`, all `Article`s | The parent enforces cross-child rules (e.g. `MaxAssetCount`) |
| **Materialize from upstream** | `static Article FromSubmission(ArticleDto dto, actors, assets, ...)` / `FromReview(...)` | Review, Production | Builds the local aggregate when an article arrives through an integration event |
| **From a creation-info interface** | `static Person Create(IPersonCreationInfo info)` | Auth `Person`, `User.Create(IUserCreationInfo, person)` | The HTTP command **and** the gRPC request both implement the interface, so there's one factory for both callers |

The creation-info interface is the cleanest of these, because the domain defines the input shape itself:

```csharp
// src/BuildingBlocks/Articles.Abstractions/IPersonCreationInfo.cs
public interface IPersonCreationInfo
{
    string Email { get; }  string FirstName { get; }  string LastName { get; }  Gender Gender { get; }
    Honorific? Honorific { get; }  string? PictureUrl { get; }  string? CompanyName { get; }
    string? Position { get; }  string? Affiliation { get; }
}

// src/BuildingBlocks/Articles.Grpc.Contracts/Auth/PersonContracts.cs
[ProtoContract]
public class CreatePersonRequest : IPersonCreationInfo { /* [ProtoMember(n)] properties */ }
```

> ⚠ The "materialize from upstream" factories take **foreign contract types** (`ArticleDto` from integration contracts, `PersonInfo` from gRPC contracts), so those Domain projects reference contract packages. It's pragmatic but couples the domain to wire formats. For new code, prefer the creation-info interface approach. See [11](11-what-not-to-copy.md).

---

## 2.4 The action object: commands as audit records

This is the most distinctive pattern in the codebase. **Every command is also a record of who did what, when, and why.** The command object itself is passed *into* domain methods.

```csharp
// src/BuildingBlocks/Blocks.Domain/IAuditableAction.cs
public interface IAuditableAction
{
    int CreatedById { get; set; }
    public bool IsAuthenticated => CreatedById != default;
    DateTime CreatedOn { get; }
    public string Action { get; }
    string? Comment { get; }
}

public interface IAuditableAction<TActionType> : IAuditableAction where TActionType : Enum
{
    TActionType ActionType { get; }
    //insight - default implementation in interfaces
    string IAuditableAction.Action => ActionType.ToString();
}

// src/BuildingBlocks/Articles.Abstractions/IArticleAction.cs
public interface IArticleAction : IAuditableAction { int ArticleId { get; } }
public interface IArticleAction<TActionType> : IAuditableAction<TActionType>, IArticleAction where TActionType : Enum;
```

```csharp
// src/BuildingBlocks/Articles.Abstractions/ArticleCommandBase.cs
public abstract record ArticleCommandBase<TActionType> : IArticleAction<TActionType> where TActionType : Enum
{
    [JsonIgnore] public int ArticleId { get; set; }                 // from the route, never the body
    public string? Comment { get; init; }                           // the only client-settable audit field
    [JsonIgnore] public abstract TActionType ActionType { get; }    // fixed per command type
    [JsonIgnore] public string Action => ActionType.ToString();
    [JsonIgnore] public DateTime CreatedOn => DateTime.UtcNow;       // computed, never trusted from client
    [JsonIgnore] public int CreatedById { get; set; }               // stamped by the pipeline from the JWT
}
```

Each service declares its own action enum (`Submission.Domain/Enums/ArticleActionType.cs`: `CreateArticle, CreateAuthor, AssignAuthor, UploadAsset, SubmitDraft, ApproveDraft, RejectDraft`), and each command fixes its type:

```csharp
public record ApproveArticleCommand : ArticleCommand
{
    public override ArticleActionType ActionType => ArticleActionType.ApproveDraft;
}
```

**What you get for free:**
1. **Audit trail.** `AddAction(action)` turns the command into an `ArticleAction` row via Mapster.
2. **Provenance can't be spoofed.** Every provenance field is `[JsonIgnore]` and filled in by the pipeline ([06 §6.7](06-cross-cutting.md#67-security-authentication-roles-two-layer-authorization)).
3. **Stage-machine input.** `action.ActionType` is the trigger checked against the transition table (§2.5).
4. **Domain events carry context.** Every event holds the action, so handlers (the timeline, emails) know who acted and why.

**How to replicate:** define `IAuditableAction` in your `Blocks.Domain`, define a `{Aggregate}CommandBase<TActionType>` in your shared kernel, and make every domain method that changes state take `IArticleAction`-style parameters instead of loose `userId, DateTime` arguments.

---

## 2.5 Lifecycle as data: the table-driven state machine

Which actions are legal in which stage is **not** coded in `if`/`switch` statements. It's a table seeded from JSON:

```jsonc
// src/Services/Submission/Submission.Persistence/Data/Master/ArticleStageTransition.json
[
  {"CurrentStage": "None",               "ActionType": "CreateArticle", "DestinationStage": "Created"},
  {"CurrentStage": "Created",            "ActionType": "UploadAsset",   "DestinationStage": "ManuscriptUploaded"},
  {"CurrentStage": "ManuscriptUploaded", "ActionType": "UploadAsset",   "DestinationStage": "ManuscriptUploaded"},
  {"CurrentStage": "ManuscriptUploaded", "ActionType": "SubmitDraft",   "DestinationStage": "Submitted"},
  {"CurrentStage": "Submitted",          "ActionType": "ApproveDraft",  "DestinationStage": "InitialApproved"},
  {"CurrentStage": "Submitted",          "ActionType": "RejectDraft",   "DestinationStage": "InitialRejected"}
]
```

The **Domain** owns the seam, an interface plus a factory delegate plus a guard:

```csharp
// src/Services/Submission/Submission.Domain/IArticleStateMachine.cs
public interface IArticleStateMachine { bool CanFire(ArticleActionType actionType); }

public delegate IArticleStateMachine ArticleStateMachineFactory(ArticleStage articleStage);

public static class Extensions
{
    public static void ValidateStageTransition(this ArticleStateMachineFactory factory, ArticleStage articleStage, ArticleActionType actionType)
    {
        if (!factory(articleStage).CanFire(actionType))
            throw new DomainException($"Action {actionType} not allowed in the {articleStage} article stage");
    }
}
```

The **Application** layer implements it, using the Stateless library over the cached table:

```csharp
// src/Services/Submission/Submission.Application/StateMachines/ArticleStateMachine.cs
public class ArticleStateMachine : IArticleStateMachine
{
    private StateMachine<ArticleStage, ArticleActionType> _stateMachine;
    public ArticleStateMachine(ArticleStage articleStage, IMemoryCache cache)
    {
        _stateMachine = new(articleStage);
        var transitions = cache.Get<List<ArticleStageTransition>>();
        foreach (var transition in transitions)
        {
            if (transition.CurrentStage != transition.DestinationStage)
                _stateMachine.Configure(transition.CurrentStage).Permit(transition.ActionType, transition.DestinationStage);
            else
                _stateMachine.Configure(transition.CurrentStage).PermitReentry(transition.ActionType);
        }
    }
    public bool CanFire(ArticleActionType actionType) => _stateMachine.CanFire(actionType);
}

// Submission.Application/DependencyInjection.cs
services.AddScoped<ArticleStateMachineFactory>(provider => articleStage =>
    new ArticleStateMachine(articleStage, provider.GetRequiredService<IMemoryCache>()));
```

The aggregate has **one** place that changes the stage, and it always validates first:

```csharp
public void SetStage(ArticleStage newStage, IArticleAction<ArticleActionType> action, ArticleStateMachineFactory stateMachineFactory)
{
    stateMachineFactory.ValidateStageTransition(Stage, action.ActionType);
    if (newStage == Stage) return;

    var currentStage = Stage;
    Stage = newStage;
    LastModifiedOn = action.CreatedOn;
    LastModifiedById = action.CreatedById;

    _stageHistories.Add(new StageHistory { ArticleId = Id, StageId = newStage, StartDate = DateTime.UtcNow });
    AddDomainEvent(new ArticleStageChanged(currentStage, newStage, action));
}
```

**Why a delegate factory?** The aggregate needs a state machine *for its current stage*, created on demand. A delegate lets the domain ask for one without knowing about DI, caches, or Stateless. It's also trivial to replace in a test (`stage => fakeMachine`).

**Why data, not code?** Adding a stage or action is a JSON change plus a migration, not a code change in every method. The rule is enforced in one place, `SetStage`.

**Variants:** Review is the same. Production builds its machine from the `DbContext` and adds a second, asset-level machine: `IAssetStateMachine.CanFire(articleStage, assetType, actionType)` with `AssetStateMachineFactory(AssetState)`, seeded with transition *conditions*.

---

## 2.6 Invariants and domain exceptions

| Violation type | Throw | Where | Example |
|---|---|---|---|
| Business rule broken | `DomainException` (or a subclass) | Aggregate behavior, state-machine guard | `"Author … is already assigned"`, `"Invitation expired."` |
| Specific, catchable rule | Subclass of `DomainException` | Domain | `TypesetterAlreadyAssignedException` (Production) |
| Malformed primitive | `Guard.ThrowIf*` → `ArgumentException` | Value-object factories | `Guard.ThrowIfFalse(IsValidEmail(value), "Invalid email format.")` |

Both map to **400 Bad Request** in the global middleware ([06 §6.1](06-cross-cutting.md#61-error-handling)). The domain never mentions HTTP.

Representative invariants to use as templates:
- **Uniqueness inside the aggregate**: `AssignAuthor`, `AssignEditor`, `AssignReviewer`, `CreateInvitation` (no open invitation for the same email).
- **Cardinality**: `CreateAsset` rejects when `assetCount >= type.MaxAssetCount`. The limit comes from reference data (§2.8), not a constant.
- **Preconditions from collections**: `Submit` requires the mandatory contribution areas.
- **Time/status**: `ReviewInvitation.Accept()` checks `Status == Open` and not expired.
- **Cross-aggregate precondition checked in the aggregate**: `Article.InviteReviewer(reviewer, action)` requires the reviewer to specialize in the article's journal.

---

## 2.7 Value objects

**Shape:** subclass `StringValueObject` or `SingleValueObject<T>`, with a **private** constructor marked `[JsonConstructor]` (for seeding and deserialization) and **static factories** that validate first.

```csharp
// src/Services/Submission/Submission.Domain/ValueObjects/EmailAddress.cs
public class EmailAddress : StringValueObject
{
    [JsonConstructor]
    private EmailAddress(string value) => Value = value;

    public static EmailAddress Create(string value)
    {
        Guard.ThrowIfNullOrWhiteSpace(value);
        Guard.ThrowIfFalse(IsValidEmail(value), "Invalid email format.");
        return new EmailAddress(value.ToLower());
    }
    // ...
}

// src/Services/Submission/Submission.Domain/ValueObjects/AssetNumber.cs
//insight - explain why class and not record. the record provides its own set of ToString, GetHashCode
// implementations and it doesn't work with inheritance
public class AssetNumber : SingleValueObject<int>
{
    [JsonConstructor] private AssetNumber(int value) => Value = value;

    public static AssetNumber Create(AssetTypeDefinition assetType, int assetCount)
    {
        int number = assetType.AllowsMultipleAssets ? assetCount + 1 : 0;
        ArgumentOutOfRangeException.ThrowIfGreaterThan(number, assetType.MaxAssetCount, "Asset type max number already reached");
        return new AssetNumber(number);
    }
    public static implicit operator int(AssetNumber assetNumber) => assetNumber.Value;   // for LINQ queries
}

// src/Services/Review/Review.Domain/Invitations/ValueObjects/InvitationToken.cs
public class InvitationToken : StringValueObject
{
    private InvitationToken(string value) => Value = value;
    public static InvitationToken CreateNew() => new InvitationToken(Base64UrlTokenGenerator.Generate());
}
```

**Rules:**
- **Classes, not records.** The base classes define equality, and records would override it (see the insight comment above).
- An invalid instance can't be built; the only way in is a factory.
- Factories are named for intent: `Create`, `CreateNew`, `From(...)`, `FromFileName(...)`, `FromAssetType(...)`, `FromSubmission(...)`.
- Add `implicit operator` to the primitive when LINQ-to-EF comparisons need it.
- Persisted with EF Core **complex properties**, not owned entities or separate tables ([05 §5.4](05-persistence.md#54-entity-configuration-a-base-class-ladder)).
- Make Mapster go through the factory too: `config.ForType<string, EmailAddress>().MapWith(src => EmailAddress.Create(src));`.

---

## 2.8 Enum + table: `EnumEntity<TEnum>`

When an enum needs **metadata that drives behavior**, it becomes a seeded entity keyed by the enum value:

```csharp
// src/Services/Submission/Submission.Domain/Entities/AssetTypeDefinition.cs
//insight - mix enums & tables together
public partial class AssetTypeDefinition : EnumEntity<AssetType>, ICacheable
{
    public required FileExtensions AllowedFileExtensions { get; init; }
    public required string DefaultFileExtension { get; init; } = default!;
    public required byte MaxAssetCount { get; init; }
    public required byte MaxFileSizeInMB { get; init; }

    public int MaxFileSizeInBytes => (MaxFileSizeInMB * 1024 * 1024);
    public bool AllowsMultipleAssets => MaxAssetCount > 1;
}
```

**Why?** Code stays readable (`AssetType.Manuscript`), the database stays joinable (foreign key on the enum's string name), and limits like `MaxAssetCount` are data an operator can change. Also `ICacheable`, so it's loaded once per process ([05 §5.8](05-persistence.md#58-caching-reference-data)). `Stage : EnumEntity<ArticleStage>` does the same for lifecycle descriptions.

**Keep plain enums** for values that rarely change and carry no metadata (`AssetState`, as the insight comment on `Asset.State` says).

---

## 2.9 Domain events

```csharp
// src/BuildingBlocks/Articles.Abstractions/DomainEvent.cs
public abstract record DomainEvent<TAction>(TAction Action) : IDomainEvent where TAction : IArticleAction;

// src/Services/Submission/Submission.Domain/Events/DomainEvent.cs  (per-service shorthand)
public record DomainEvent(IArticleAction Action) : DomainEvent<IArticleAction>(Action);

// src/Services/Submission/Submission.Domain/Events/ArticleApproved.cs
public record ArticleApproved(Article Article, IArticleAction Action) : DomainEvent(Action);

// src/BuildingBlocks/Articles.Abstractions/Events/ArticleStageChanged.cs  (shared: every service raises it)
public record ArticleStageChanged(ArticleStage CurrentStage, ArticleStage NewStage, IArticleAction Action)
    : DomainEvent<IArticleAction>(Action);
```

| Rule | Why |
|---|---|
| **Records, past-tense names** (`ArticleApproved`, `AuthorAssigned`, `ReviewerInvited`, `FileUploaded`) | Immutable facts |
| **Carry the aggregate and the action** | In-process handlers can use the full object graph; the action tells them who and why |
| **Raised inside behavior methods only** | The rule and its announcement can't drift apart |
| **Stay inside the service boundary** | They hold entities. To cross services, a handler converts them into an integration event ([04 §4.4](04-service-communication.md#44-the-domain--integration-handoff)) |
| `ArticleStageChanged` lives in the shared kernel | Generic reactors (the ArticleTimeline module) can handle it for any service |

Handlers are named `{Effect}On{Event}Handler` and live in the feature folder they serve. Examples: `SendConfirmationEmailOnReviewerAssignedHandler`, `NotifyProductionOfficeOnArticleAcceptedHandler`, `PublishIntegrationEventOnArticleApprovedHandler`.

---

## 2.10 Write model vs read model

The write services have rich aggregates. **ArticleHub deliberately doesn't:**

```csharp
// src/Services/ArticleHub/ArticleHub.Domain/Entities/Article.cs
public class Article : Entity
{
    public required string Title { get; set; }
    public ArticleStage Stage { get; set; }
    public required int JournalId { get; set; }
    public Journal Journal { get; set; } = null!;
    public List<ArticleActor> Actors { get; set; } = new();
    // ... public get/set only, no methods
}
```

**Rule:** a projection has no invariants to protect, because the source services already enforced them. Adding DDD machinery to a read model only adds ceremony.

---

## 2.11 Organizing the Domain project

Two layouts coexist:

| Layout | Used by | Shape |
|---|---|---|
| By **type** | Submission | `Entities/`, `Behaviors/`, `Events/`, `ValueObjects/`, `Enums/` |
| By **aggregate** | Review, Production, Auth, Journals | `Articles/{Article.cs, Behaviors/, Events/}`, `Invitations/{…, ValueObjects/, Enums/}`, `Reviewers/…`, `_Shared/` |

**Recommendation for new projects: by aggregate.** Four of the five domains that aren't read models use it. It applies the vertical-slice idea inside the Domain (everything about `ReviewInvitation` is under `Invitations/`), and it scales better once a service has several aggregates. Put types shared across aggregates (the per-service `IArticleAction`, `DomainEvent`, `Person`, `Journal`, `ArticleActionType`) in `_Shared/`.

```
Review.Domain/
├── Articles/        Article.cs, Behaviors/Article.cs, Events/*.cs, ArticleActor.cs, Stage.cs, ...
├── Assets/          Asset.cs, Behaviors/, Enums/, Events/, ValueObjects/
├── Invitations/     ReviewInvitation.cs, Behaviors/, Enums/, Events/, ValueObjects/
├── Reviewers/       Reviewer.cs, Behaviors/, Events/
├── _Shared/         DomainEvent.cs, IArticleAction.cs, Journal.cs, Person.cs, Enums/, ValueObjects/
├── IArticleStateMachine.cs
└── GlobalUsings.cs
```

---

## 2.12 Replicating the domain model: summary

1. Copy `Blocks.Domain` as it is.
2. For each aggregate: `{Name}.cs` (state, `partial`, restricted constructor) + `Behaviors/{Name}.cs` (factories + rule methods).
3. Every state-changing method takes an action (`IArticleAction<TActionType>`), checks invariants, mutates, calls `AddAction`, and raises a past-tense event.
4. Lifecycle transitions: interface + factory delegate + `Validate…` guard in Domain, implementation in Application, transition table as master data.
5. Value objects: classes with a private constructor, `[JsonConstructor]`, and validating static factories.
6. Enums with metadata become `EnumEntity<T>` + `ICacheable`.
7. Read models get plain classes.
