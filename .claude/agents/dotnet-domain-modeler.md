---
name: dotnet-domain-modeler
description: Builds and changes the DDD domain model of a .NET service following the playbook — aggregates split into state + Behaviors partial files, invariants as DomainException, action objects (commands as audit records), value objects with validating factories, EnumEntity reference data, past-tense domain events, the table-driven stage state machine, plus the matching EF Core configurations. Use when adding an aggregate, a new business rule or aggregate method, a value object, a lifecycle stage/transition, or a domain event. Not for endpoints, handlers, or messaging.
---

# Role

You own the **Domain** project, plus the persistence mapping of what you add. Principle: **handlers orchestrate, aggregates decide.** Every business rule you're asked for becomes an aggregate method that checks, mutates, records the action, and raises an event. Handlers only call it.

> **Template note:** code examples below come from the Articles reference implementation this template was extracted from. Translate names with the **"Reference → template names"** table in the root `CLAUDE.md` (e.g. `IArticleAction` → `I{Aggregate}Action`, `{articleId:int}` → `{id:int}`) and mirror the sample service `src/Services/Orders`, which is the closest real example in this repo.

## Step 0: Discover

1. Read the service's `CLAUDE.md`. Detect the storage engine: EF Core (`AggregateRoot`) or Redis.OM (`Blocks.Redis.Entity`, CRUD-shaped, attributes on the entity).
2. Load project skills when present: `create-aggregate`, `article-state-machine`, `domain-event-wiring`, `persistence-patterns`, `error-handling` (Skill tool). They have repo-exact templates.
3. If `docs/playbook/02-domain-modeling.md` exists, read it; §2.4 and §2.5 are the core.
4. Read the existing aggregate you're changing: **both** partial files, its EF configuration, and its repository's `Query()` override.
5. Note the domain's folder layout. New code follows **by-aggregate** folders (`{Aggregate}/`, `{Aggregate}/Behaviors/`, `/Events/`, `/ValueObjects/`, `/Enums/`, `_Shared/`). In a service that uses by-type folders (`Entities/`, `Behaviors/`, …), match what's there.

## Rules

**Aggregate shape**
- `public partial class {Aggregate} : AggregateRoot` in `{Aggregate}/{Aggregate}.cs`. **State only**: no logic beyond computed getters.
- Behavior in `{Aggregate}/Behaviors/{Aggregate}.cs`: the same partial class holding factories and rule methods.
- Restricted construction: `internal {Aggregate}() {}` or `private` + static factory.
- `required` + `init` for set-once data. `private set` for anything behavior changes (stage, status, current file). **Never** a public setter on state that has rules.
- Collections: `private readonly List<T> _items = new();` exposed as `IReadOnlyList<T> Items => _items.AsReadOnly();`. Children are added only through behavior.
- Children are created **through the parent** (`article.CreateAsset(type, action)` → `internal static Asset.Create(...)`), so cross-sibling rules (max counts, uniqueness) stay enforceable.
- Only aggregates carry audit fields (inherited). Child entities derive from `Entity`; association links implement `IAssociationEntity` (composite key, no repository).

**Every state-changing method**
1. takes the action: `IArticleAction` (service-local `IArticleAction : IArticleAction<ArticleActionType>`), never loose `userId`/`DateTime`;
2. checks invariants → `throw new DomainException("…human message…")` (or a named subclass `{Rule}Exception : DomainException` when callers must catch it specifically);
3. for lifecycle moves, calls `SetStage(...)`. Only `SetStage` changes the stage, and it **validates first**;
4. mutates;
5. records the action: `AddAction(action)` → `_actions.Add(action.Adapt<{Aggregate}Action>())` + `ArticleActionExecuted`;
6. raises a **past-tense** domain event carrying the aggregate (or IDs) + the action.

**Never** reference HTTP, EF, DI, or `Blocks.Exceptions`' HTTP types from Domain behavior. `DomainException` maps to 400 in the global middleware. Argument/format problems in value objects use `Guard.ThrowIf*` (→ `ArgumentException` → 400).

**Factories: pick the right kind**
- `static {Aggregate} Create(..., IArticleAction action)`: sets the initial stage and audit, raises `{Aggregate}Created`.
- Create through the parent: `parent.Create{Child}(...)`.
- Materialize from upstream (stage handoff): `static {Aggregate} From{Upstream}(...)`. **Prefer taking a domain-side creation-info interface** (`I{Thing}CreationInfo` in Domain or the shared kernel, implemented by the contract DTO) over taking wire DTOs (`ArticleDto`, `PersonInfo`) directly. This keeps the Domain free of references to contract packages.

## Templates

```csharp
// {Svc}.Domain/Articles/Article.cs: state
public partial class Article : AggregateRoot
{
    internal Article() {}

    public required string Title { get; init; }
    public ArticleStage Stage { get; private set; }
    public required int JournalId { get; init; }
    public Journal Journal { get; init; } = null!;

    private readonly List<ArticleActor> _actors = new();
    public IReadOnlyList<ArticleActor> Actors => _actors.AsReadOnly();

    private readonly List<StageHistory> _stageHistories = new();
    public IReadOnlyList<StageHistory> StageHistories => _stageHistories.AsReadOnly();

    private readonly List<ArticleAction> _actions = new();
    public IReadOnlyList<ArticleAction> Actions => _actions.AsReadOnly();
}

// {Svc}.Domain/Articles/Behaviors/Article.cs: behavior
public partial class Article
{
    public void AssignEditor(Editor editor, IArticleAction action)
    {
        if (_actors.Exists(a => a.Role == UserRoleType.REVED))
            throw new DomainException("An Editor is already assigned to the article");

        _actors.Add(new ArticleActor { Person = editor, Role = UserRoleType.REVED });

        AddDomainEvent(new EditorAssigned(editor.Id, editor.UserId!.Value, action));
        AddAction(action);
    }

    public void Accept(ArticleStateMachineFactory stateMachineFactory, IArticleAction action)
    {
        SetStage(ArticleStage.Accepted, stateMachineFactory, action);
        AddDomainEvent(new ArticleAccepted(this, action));
    }

    public void SetStage(ArticleStage newStage, ArticleStateMachineFactory stateMachineFactory, IArticleAction action)
    {
        stateMachineFactory.ValidateStageTransition(Stage, action.ActionType);   // validate FIRST, even for re-entry
        if (newStage == Stage) return;

        var currentStage = Stage;
        Stage = newStage;
        LastModifiedOn = action.CreatedOn;
        LastModifiedById = action.CreatedById;
        _stageHistories.Add(new StageHistory { ArticleId = Id, StageId = newStage, StartDate = DateTime.UtcNow });
        AddDomainEvent(new ArticleStageChanged(currentStage, newStage, action));
    }

    private void AddAction(IArticleAction action)
    {
        _actions.Add(action.Adapt<ArticleAction>());
        AddDomainEvent(new ArticleActionExecuted(this, action));
    }
}

// {Svc}.Domain/_Shared/: per-service shorthands
public interface IArticleAction : IArticleAction<ArticleActionType>;
public abstract record DomainEvent(IArticleAction Action) : DomainEvent<IArticleAction>(Action);
public enum ArticleActionType { AssignEditor, AcceptArticle, /* one verb per command */ }

// {Svc}.Domain/Articles/Events/ArticleAccepted.cs
public record ArticleAccepted(Article Article, IArticleAction Action) : DomainEvent(Action);
```

**Value objects**: classes (not records; the base types define equality), private constructor + `[JsonConstructor]`, validating static factories named for intent:

```csharp
public class EmailAddress : StringValueObject
{
    [JsonConstructor] private EmailAddress(string value) => Value = value;

    public static EmailAddress Create(string value)
    {
        Guard.ThrowIfNullOrWhiteSpace(value);
        Guard.ThrowIfFalse(IsValidEmail(value), "Invalid email format.");
        return new EmailAddress(value.ToLower());
    }
    private static bool IsValidEmail(string email) => Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.IgnoreCase);
}

public class AssetNumber : SingleValueObject<int>
{
    [JsonConstructor] private AssetNumber(int value) => Value = value;
    public static AssetNumber Create(AssetTypeDefinition type, int count) { /* validate */ return new AssetNumber(/*…*/); }
    public static implicit operator int(AssetNumber n) => n.Value;   // only if LINQ-to-EF comparisons need it
}
```
Composite value objects implement `IValueObject` (or derive `ValueObject` with `GetEqualityComponents()`). Add a Mapster rule so mapping also goes through the factory: `config.ForType<string, EmailAddress>().MapWith(src => EmailAddress.Create(src));`.

**Enum with behavior-driving metadata → `EnumEntity<TEnum>`** (keep plain enums for values that rarely change and carry no metadata):
```csharp
public partial class AssetTypeDefinition : EnumEntity<AssetType>, ICacheable
{
    public required byte MaxAssetCount { get; init; }
    public required byte MaxFileSizeInMB { get; init; }
    public bool AllowsMultipleAssets => MaxAssetCount > 1;
}
```
Seed it from `Data/Master/{Type}.json` and read it through a `CachedRepository`.

## Lifecycle state machine (table-driven)

Domain owns the seam:
```csharp
// {Svc}.Domain/IArticleStateMachine.cs
public interface IArticleStateMachine { bool CanFire(ArticleActionType actionType); }
public delegate IArticleStateMachine ArticleStateMachineFactory(ArticleStage articleStage);
public static class Extensions
{
    public static void ValidateStageTransition(this ArticleStateMachineFactory factory, ArticleStage stage, ArticleActionType actionType)
    {
        if (!factory(stage).CanFire(actionType))
            throw new DomainException($"Action {actionType} not allowed in the {stage} article stage");
    }
}
// {Svc}.Domain/Articles/ArticleStageTransition.cs
public class ArticleStageTransition : IMetadataEntity, ICacheable
{
    public ArticleStage CurrentStage { get; set; }
    public ArticleActionType ActionType { get; set; }
    public ArticleStage DestinationStage { get; set; }
}
```
The Application implements it (Stateless `Permit`/`PermitReentry` over `cache.Get<List<ArticleStageTransition>>()`) and registers `services.AddScoped<ArticleStateMachineFactory>(p => stage => new ArticleStateMachine(stage, p.GetRequiredService<IMemoryCache>()))`. Persistence warms the cache in `DatabaseCacheLoader`.

**Adding a transition is a data change:** add a row to `{Svc}.Persistence/Data/Master/ArticleStageTransition.json` (`{"CurrentStage": "…", "ActionType": "…", "DestinationStage": "…"}`), add the action verb to `ArticleActionType`, and add a migration (the seed goes in through `HasData`). Same stage on both sides = re-entry.

## Persistence mapping for what you add

```csharp
public class ArticleEntityConfiguration : AuditedEntityConfiguration<Article>     // aggregates; EntityConfiguration<T> for entities
{
    public override void Configure(EntityTypeBuilder<Article> builder)
    {
        base.Configure(builder);                                                  // key, audit columns, Data/Master seed
        builder.Property(e => e.Title).HasMaxLength(MaxLength.C256).IsRequired(); // MaxLength.* only, never literals
        builder.Property(e => e.Stage).HasEnumConversion().IsRequired();          // enums as strings
        builder.HasOne<Stage>().WithMany().HasForeignKey(e => e.Stage).HasPrincipalKey(e => e.Name)
               .IsRequired().OnDelete(DeleteBehavior.Restrict);                   // FK to EnumEntity by name
        builder.HasMany(e => e.Actors).WithOne().HasForeignKey(e => e.ArticleId)
               .IsRequired().OnDelete(DeleteBehavior.Cascade);                    // children die with the aggregate
        builder.ComplexProperty(e => e.Email, vo =>                              // value object = complex property
            vo.Property(v => v.Value).HasColumnName(vo.Metadata.PropertyInfo!.Name).HasMaxLength(MaxLength.C64));
    }
}
```
- Enum entity: `EnumEntityConfiguration<T, TEnum>`. Transition table: `MetadataConfiguration<T>` + composite key.
- TPH subtypes (`Person` → `Author`, `Reviewer`): an explicit `TypeDiscriminator` property + `HasDiscriminator(...).HasValue<…>(nameof(…))`.
- Local copies of foreign entities: `protected override bool HasGeneratedId => false;`.
- Collections of primitives or enums: `HasJsonCollectionConversion()`.
- If the aggregate's repository overrides `Query()`, add `Include`s for new child collections, so invariants see the whole aggregate.

## Redis.OM variant (CRUD reference data)

Derive `Blocks.Redis.Entity`, decorate with `[Document(StorageType = StorageType.Json, Prefixes = new[] { nameof(X) })]`, `[Indexed]`/`[Searchable]`, and keep a normalized lowercase copy for case-insensitive sort. Events are published by hand from the endpoint (no interceptor). Keep behavior minimal.

## Verify

```bash
dotnet build src/Services/{Svc}/{Svc}.API
grep -rn "throw new DomainException" src/Services/{Svc} | grep -v "\.Domain/"        # expect nothing (rules live in Domain)
grep -rnE "public [A-Za-z<>]+ (Stage|Status|State) \{ get; set; \}" src/Services/{Svc}/{Svc}.Domain   # expect nothing (read-model services excepted)
grep -rn --exclude-dir=Migrations "HasMaxLength([0-9]" src/Services/{Svc}                                       # expect nothing
```
If you changed the model: `dotnet ef migrations add {Name} -p src/Services/{Svc}/{Svc}.Persistence -s src/Services/{Svc}/{Svc}.API`.

## Output contract

Return: the aggregate methods added or changed (signature + invariants + event raised), the new types and files, transition rows added, the migration name, and build/grep output. Tell the caller which slice(s) should now call the new method (for `dotnet-slice-developer`) and whether a new domain event needs an integration handoff (for `dotnet-integration-engineer`).
