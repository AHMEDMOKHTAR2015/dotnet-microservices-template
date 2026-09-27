# 05 · Persistence

> **Pattern library, part 5.** Database per service, EF Core conventions, repositories, domain-event dispatch, seeding, caching, migrations, and the two non-EF stores.

---

## 5.1 A database per service; the engine follows the role

| Service | Engine | Why this engine (as the code shows it) |
|---|---|---|
| Submission, Review, Production, Auth | **SQL Server** + EF Core | Write-side aggregates with real invariants, relationships, transactions |
| ArticleHub | **PostgreSQL** + EF Core + **Hasura** | Read model: Hasura puts an instant GraphQL query API on Postgres tables |
| Journals | **Redis Stack** (Redis.OM) | Small CRUD-shaped reference data with full-text search. No EF machinery at all. |
| Files (Submission, Review) | **MongoDB GridFS** | Binary storage per stage |
| Files (Production) | **Azure Blob** | Final published assets |

**Rule:** reserve EF Core's full machinery (aggregates, interceptors, repository base) for services with real write-side invariants. CRUD-shaped data gets a document store; read models get whatever makes querying cheapest.

---

## 5.2 The DbContext

```csharp
// src/Services/Submission/Submission.Persistence/SubmissionDbContext.cs
public partial class SubmissionDbContext(DbContextOptions<SubmissionDbContext> options, IMemoryCache cache)
    : ApplicationDbContext<SubmissionDbContext>(options, cache)
{
    #region Entities
    public virtual DbSet<Article> Articles { get; set; }
    public virtual DbSet<ArticleActor> ArticleActors { get; set; }
    public virtual DbSet<Asset> Assets { get; set; }
    public virtual DbSet<AssetTypeDefinition> AssetTypes { get; set; }
    public virtual DbSet<Journal> Journals { get; set; }
    public virtual DbSet<Person> Persons { get; set; }
    public virtual DbSet<Author> Authors { get; set; }
    // ...
    #endregion

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(this.GetType().Assembly);   // pick up every IEntityTypeConfiguration
        modelBuilder.UseEntityTypeNamesAsTables();                               // table = CLR type name (TPH root)
    }

    public async override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        this.UnTrackCacheableEntities();          // never write cached reference rows back
        return await base.SaveChangesAsync(ct);
    }
}
```

| Rule | Detail |
|---|---|
| Name: `{Service}DbContext` | One per service, no generic `AppDbContext` |
| Base: `ApplicationDbContext<TSelf>` | Adds `GetAllCached<T>()` / `GetByIdCached<T,TId>()` for `ICacheable` reference data |
| `ApplyConfigurationsFromAssembly` | One configuration class per entity, discovered automatically |
| `UseEntityTypeNamesAsTables()` | Tables are **singular CLR names** (`Article`, `Asset`), and all TPH subtypes map to the root's table. An optional `INameRewriter` handles casing (ArticleHub passes `SnakeCaseNameRewriter`). |
| `UnTrackCacheableEntities()` in `SaveChangesAsync` | Cached `ICacheable` entities attached to new rows are forced to `Unchanged` |

Identity is the exception: `AuthDbContext : IdentityDbContext<User, Role, int>`, because ASP.NET Identity requires its own base.

---

## 5.3 Registration: shared connection, interceptors from DI

```csharp
// src/Services/Submission/Submission.Persistence/DependencyInjection.cs
public static IServiceCollection AddPersistenceServices(this IServiceCollection services, IConfiguration configuration)
{
    var connectionString = configuration.GetConnectionString("Database");

    services.AddScoped<ISaveChangesInterceptor, DispatchDomainEventsInterceptor>();

    // one DbConnection per scope: every DbContext in the request can share it (and its transaction)
    services.AddScoped<DbConnection>(provider => new SqlConnection(connectionString));
    services.AddDbContext<SubmissionDbContext>((provider, options) =>
    {
        var dbConnection = provider.GetRequiredService<DbConnection>();
        options.AddInterceptors(provider.GetServices<ISaveChangesInterceptor>());
        options.UseSqlServer(dbConnection);
    });

    services.AddScoped<TransactionProvider>();

    services.AddScoped(typeof(Repository<>));             // generic repository for simple entities
    services.AddDerivedTypesOf(typeof(Repository<>));      // every ArticleRepository/AssetRepository/... automatically
    services.AddScoped<AssetTypeRepository>();             // cached repository

    services.AddHostedService<DatabaseCacheLoader>();      // warm reference-data caches at startup
    return services;
}
```

**Why a scoped `DbConnection`?** The ArticleTimeline module has its **own** `DbContext` but must write in the **same transaction** as the host. Both contexts are built on the one scoped connection, and `TransactionProvider` hands out the shared transaction ([5.6](#56-domain-event-dispatch-savechanges-interceptors)).

The connection string is always named **`Database`**; the design-time factory relies on that ([5.9](#59-migrations)).

---

## 5.4 Entity configuration: a base-class ladder

```
IEntityTypeConfiguration<T>
 ├── EntityConfiguration<T, TKey>       HasKey(Id) + SeedFromJsonFile() (Data/Master/{T}.json)
 │    ├── EntityConfiguration<T>        + HasGeneratedId toggle (default: identity column)
 │    ├── AuditedEntityConfiguration<T> audit columns required (T : IAggregateRoot) + opt-in RowVersion
 │    └── EnumEntityConfiguration<T,E>  unique Name (enum stored as string) + Description
 └── MetadataConfiguration<T>           ToTable(typeof(T).Name) + seed (for IMetadataEntity)
```

```csharp
// src/BuildingBlocks/Blocks.EntityFrameworkCore/EntityConfigurations/AuditedEntityConfiguration.cs
public abstract class AuditedEntityConfiguration<T, TKey> : EntityConfiguration<T, TKey>
    where T : class, IEntity<TKey>, IAggregateRoot<TKey> where TKey : struct
{
    protected virtual string DefaultDateSql => "GETUTCDATE()";
    protected virtual bool HasConcurrencyToken => false;          // opt-in optimistic concurrency

    public override void Configure(EntityTypeBuilder<T> builder)
    {
        base.Configure(builder);
        builder.Property(e => e.CreatedOn).IsRequired().HasDefaultValueSql(DefaultDateSql);
        builder.Property(e => e.CreatedById).IsRequired();
        builder.Property(e => e.LastModifiedOn);
        builder.Property(e => e.LastModifiedById);
        if (HasConcurrencyToken)
            builder.Property<byte[]>("RowVersion").IsRowVersion();
    }
}
```

A service configuration only adds what's specific to it:

```csharp
// src/Services/Submission/Submission.Persistence/EntityConfigurations/ArticleEntityConfiguration.cs
public class ArticleEntityConfiguration : AuditedEntityConfiguration<Article>
{
    public override void Configure(EntityTypeBuilder<Article> builder)
    {
        base.Configure(builder);

        builder.HasIndex(e => e.Title);
        builder.Property(e => e.Title).HasMaxLength(MaxLength.C256).IsRequired();
        builder.Property(e => e.Scope).HasMaxLength(MaxLength.C2048).IsRequired();
        builder.Property(e => e.Stage).HasEnumConversion().IsRequired();

        builder.HasOne<Stage>().WithMany()                 // FK to the EnumEntity by its *name*
             .HasForeignKey(e => e.Stage).HasPrincipalKey(e => e.Name)
             .IsRequired().OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Journal).WithMany(e => e.Articles)
            .HasForeignKey(e => e.JournalId).IsRequired().OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.Assets).WithOne(e => e.Article)
            .HasForeignKey(e => e.ArticleId).IsRequired().OnDelete(DeleteBehavior.Cascade);   // children die with the aggregate
        // ... Actors, StageHistories, Actions: Cascade
    }
}
```

### Mapping conventions

| Situation | Convention | Example |
|---|---|---|
| String length | Always `MaxLength.C*` constants (C8 … C2048), never literals | `HasMaxLength(MaxLength.C64)` |
| Enums | Stored **as strings**: `HasEnumConversion()` | `Stage`, `Type`, `Role`, `TypeId` |
| Value object | EF Core **complex property**, column named after the property | see below |
| Collection of primitives/enums | JSON column: `HasJsonCollectionConversion()` / `BuildJsonReadOnlyListConvertor<T>()` | `ArticleAuthor.ContributionAreas`, `AllowedFileExtensions` |
| Inheritance | **TPH** with an explicit `TypeDiscriminator` property | `Person`/`Author`, `ArticleActor`/`ArticleAuthor` |
| Association entity | Composite key, no own repository | `ArticleActor`: `HasKey(e => new { e.ArticleId, e.PersonId, e.Role })` |
| Local copy of foreign data | `HasGeneratedId => false` (ID comes from the owning service) | `JournalEntityConfiguration`, `PersonEntityConfiguration` |
| Aggregate → children | `OnDelete(Cascade)`; references to other aggregates `Restrict` | above |
| Documentation | `.HasComment("…")` on non-obvious columns | `Author.Discipline`, `File.Size` |
| Case-insensitive lookups | Column collation | `Email` → `UseCollation("SQL_Latin1_General_CP1_CI_AS")` |

Value objects as complex properties:

```csharp
// src/Services/Submission/Submission.Persistence/EntityConfigurations/AssetEntityConfiguration.cs
builder.ComplexProperty(e => e.Name, builder =>
{
    builder.Property(vo => vo.Value)
        .HasColumnName(builder.Metadata.PropertyInfo!.Name)   // column "Name", not "Name_Value"
        .HasMaxLength(MaxLength.C64).IsRequired();
});

builder.ComplexProperty(e => e.File, fileBuilder => new FileEntityConfiguration().Configure(fileBuilder));   // nested VO config reused
```

---

## 5.5 Repositories: the repository is the unit of work

```
RepositoryBase<TContext, TEntity, TKey>  (Blocks.EntityFrameworkCore)
  Query() / QueryNotTracked() / FindByIdAsync / GetByIdAsync / ExistsAsync / AddAsync / Update / UpsertAsync
  Remove / DeleteByIdAsync (raw SQL) / *Range / SaveChangesAsync / ClearTracking
      ▲
Repository<TEntity>  (per service, closes TContext):   public class Repository<TEntity>(SubmissionDbContext dbContext) : RepositoryBase<SubmissionDbContext, TEntity>(dbContext)
      ▲
ArticleRepository, PersonRepository, AssetRepository …  (only when an aggregate needs includes or custom queries)
```

```csharp
// src/Services/Submission/Submission.Persistence/Repositories/ArticleRepository.cs
public class ArticleRepository(SubmissionDbContext dbContext) : Repository<Article>(dbContext)
{
    public override IQueryable<Article> Query()                 // the default load includes the aggregate's children
        => base.Entity.Include(e => e.Actors).ThenInclude(e => e.Person).Include(e => e.Assets);

    public async Task<Article?> GetFullArticleByIdAsync(int id, CancellationToken ct = default)
        => await Query().Include(e => e.Journal).Include(e => e.SubmittedBy).SingleOrDefaultAsync(e => e.Id == id, ct);
}
```

| Rule | Why |
|---|---|
| **No repository interfaces** | Guardrail: "No interfaces without multiple implementations for repositories." Handlers inject `ArticleRepository` directly. |
| **No `IUnitOfWork`** | `SaveChangesAsync` lives on the repository. You save through the main aggregate's repository, and it commits everything tracked by the shared DbContext. |
| **Override `Query()` to define the aggregate boundary** | Every `GetByIdAsync` loads the aggregate with its children, so invariants see the whole collection |
| `FindByIdAsync` (EF `Find`, no includes) vs `GetByIdAsync` (`Query()` + includes) | Use `Find` when you only need the root (e.g. `Approve`, `Reject`) |
| `DeleteByIdAsync` uses raw SQL | `//insight … setting the state to Deleted doesn't work because it requires to instantiate an empty entity first which is not possible if the entity has required properties` |
| Registered by scanning: `AddDerivedTypesOf(typeof(Repository<>))` | New repositories need no DI line |

Throwing lookups are **extension methods**, not repository members, so they work on both repositories and `DbSet`s:

```csharp
// src/BuildingBlocks/Blocks.EntityFrameworkCore/Extensions/RepositoryExtensions.cs
public static async Task<TEntity> FindByIdOrThrowAsync<TEntity, TContext>(this RepositoryBase<TContext, TEntity> repository, int id) ...
    => Guard.NotFound(await repository.FindByIdAsync(id));                      // → 404

public static async Task<TEntity> FindByIdOrThrowAsync<TEntity>(this DbSet<TEntity> dbSet, int id) ...
    => Guard.NotFound(await dbSet.FindAsync(id));

public static async Task EnsureNotExistsOrThrowAsync<TEntity, TContext>(this RepositoryBase<TContext, TEntity> repository, int id, CancellationToken ct = default) ...
{
    if (await repository.ExistsAsync(id, ct))
        throw new BadRequestException($"{typeof(TEntity).Name}({id}) already exists");   // → 400 (idempotency guard)
}
```

---

## 5.6 Domain-event dispatch: SaveChanges interceptors

Domain events are collected on aggregates ([02 §2.1](02-domain-modeling.md#21-the-base-types-blocksdomain)) and dispatched by an EF Core interceptor through the `IDomainEventPublisher` seam:

```csharp
// src/BuildingBlocks/Blocks.EntityFrameworkCore/Extensions/DbContextExtensions.DomainEvents.cs
public static async Task<int> DispatchDomainEventsAsync(this DbContext ctx, IDomainEventPublisher eventPublisher, CancellationToken ct = default)
{
    var aggregates = ctx.ChangeTracker.Entries().Select(a => a.Entity)
        .OfType<IAggregateRoot>().Where(a => a.DomainEvents.Any()).ToList();
    if (aggregates.IsEmpty()) return 0;

    var domainEvents = aggregates.SelectMany(a => a.DomainEvents).ToList();
    aggregates.ForEach(a => a.ClearDomainEvents());                 // clear first: handlers that save again won't re-dispatch
    foreach (var domainEvent in domainEvents)
        await eventPublisher.PublishAsync(domainEvent, ct);
    return domainEvents.Count;
}
```

Two interceptors; choose one per service:

| Interceptor | Timing | Handlers' own writes | Used by | Choose when |
|---|---|---|---|---|
| [`DispatchDomainEventsInterceptor`](../../src/BuildingBlocks/Blocks.EntityFrameworkCore/Interceptors/DispatchDomainEventsInterceptor.cs) | `SavedChangesAsync`, **after commit** | Separate save, separate transaction | Submission, Review | Handlers do side effects (emails, publishing) that must only happen after a successful commit |
| [`TransactionalDispatchDomainEventsInterceptor`](../../src/BuildingBlocks/Blocks.EntityFrameworkCore/Interceptors/TransactionalDispatchDomainEventsInterceptor.cs) | Opens a transaction in `SavingChangesAsync`, dispatches in `SavedChangesAsync`, **then commits**; rolls back in `SaveChangesFailedAsync` | **Same transaction** as the trigger | Production | Handlers write data that must be atomic with the change (the ArticleTimeline entry) |

The transactional variant is enabled by `TransactionOptions.UseSingleTransaction` (config section `TransactionOptions`). A handler in another DbContext joins the transaction like this:

```csharp
// src/Modules/ArticleTimeline/.../EventHandlers/AddTimelineEventHandler.cs
_dbContext.Database.UseTransaction(await _transactionProvider.GetCurrentTransaction(ct));
```

**The publisher is swappable:** `IDomainEventPublisher` has a MediatR implementation (`mediator.Publish`: Submission, Review, Production) and a FastEndpoints implementation (`@event.PublishAsync(Mode.WaitForAll)`: Auth, Journals). Because `IDomainEvent : INotification, IEvent`, an event class works with either.

**Services without the interceptor** (Journals on Redis, Auth's Identity flow) publish domain events **by hand** from the endpoint: `await PublishAsync(new JournalCreated(journal));`.

---

## 5.7 Seeding: two channels

| Channel | Data | Mechanism | When it runs |
|---|---|---|---|
| **Master data** | Reference/config rows every environment needs: stages, asset types, transitions | `EntityConfiguration.Configure` → `builder.SeedFromJsonFile()` reads `Data/Master/{Entity}.json` → EF `HasData` | Built into **migrations** |
| **Test data** | Demo persons, journals | `app.Services.SeedTestData()` → `context.SeedFromJsonFile<T>()` reads `Data/Test/{Entity}.json` | At startup, **Development only**, skipped if the table already has rows |

```csharp
// src/Services/Submission/Submission.Persistence/Data/Test/Seed.cs
public static void SeedTestData(this IServiceProvider services)
{
    services.SeedTestData<SubmissionDbContext>(context =>      // wraps the whole thing in one transaction
    {
        context.SeedFromJsonFile<Person>();
        context.SeedFromJsonFile<Journal>();
    });
}
```

- The file name is the entity type name, so there's no mapping code.
- Test JSON can instantiate TPH subtypes with `"$type": "Submission.Domain.Entities.Author, Submission.Domain"` (Newtonsoft `TypeNameHandling.Auto` + a private-member contract resolver that can reach `private set`).
- **Master data shared by several services** lives once in `src/SharedData/Master/` and is **linked** into each Persistence project:
  ```xml
  <Content Include="..\..\..\SharedData\Master\Stage.json" Link="Data\Master\Stage.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
  ```
- JSON seed files must be marked `CopyToOutputDirectory` (they're read from `AppContext.BaseDirectory`).

---

## 5.8 Caching reference data

Small tables that rarely change (`AssetTypeDefinition`, `ArticleStageTransition`) implement the marker `ICacheable` and are cached **whole, per process, keyed by type**:

```csharp
// src/BuildingBlocks/Blocks.Core/Cache/MemoryCacheExtensions.cs
public static T GetOrCreateByType<T>(this IMemoryCache memoryCache, Func<ICacheEntry, T> factory)
    => memoryCache.GetOrCreate(typeof(T).FullName!, factory)!;

// src/BuildingBlocks/Blocks.EntityFrameworkCore/Repositories/CachedRepository.cs
public abstract class CachedRepository<TDbContext, TEntity, TId>(TDbContext _dbContext, IMemoryCache _cache)
    where TEntity : class, IEntity<TId>, ICacheable ...
{
    public IEnumerable<TEntity> GetAll() => _cache.GetOrCreateByType(entry => _dbContext.Set<TEntity>().AsNoTracking().ToList());
    public TEntity GetById(TId id) => GetAll().Single(e => e.Id.Equals(id));
}

// src/Services/Submission/Submission.Persistence/Repositories/AssetTypeRepository.cs
public class AssetTypeRepository(SubmissionDbContext dbContext, IMemoryCache cache)
    : CachedRepository<SubmissionDbContext, AssetTypeDefinition, AssetType>(dbContext, cache);
```

A hosted service warms the cache at startup, so the state machine can read transitions synchronously:

```csharp
// src/Services/Submission/Submission.Persistence/DatabaseCacheLoader.cs
public Task StartAsync(CancellationToken cancellationToken)
{
    using var scope = _serviceProvider.CreateScope();
    scope.ServiceProvider.GetRequiredService<SubmissionDbContext>().GetAllCached<ArticleStageTransition>();
    return Task.CompletedTask;
}
```

**Rule:** a type-only cache key rules out, by design, caching anything that varies per request or per entity instance. Only whole reference tables qualify. There's no eviction: changing reference data means a new migration and a restart.

---

## 5.9 Migrations

```bash
dotnet ef migrations add Name  -p Services/{Svc}/{Svc}.Persistence -s Services/{Svc}/{Svc}.API
dotnet ef database update      -p Services/{Svc}/{Svc}.Persistence -s Services/{Svc}/{Svc}.API
```

- Migrations live in the **Persistence** project; the **API** project is the startup project.
- Applied **at startup**: `app.Migrate<SubmissionDbContext>();` (the code carries `//insight - explain when is the best time to run the migration, integrate the migration in the CI pipeline`, so this is a teaching convenience).
- An embedded module keeps **its own migrations and history table** on the host's database:
  ```csharp
  options.UseSqlServer(dbConnection, o => o.MigrationsHistoryTable("__EFMigrationsHistory", "ArticleTimeline"));
  // Production Program.cs
  app.Migrate<ProductionDbContext>();
  app.Migrate<ArticleTimelineDbContext>();
  ```
- For a context without a usable DI setup at design time, derive from `DesignTimeFactoryBase<TContext>` and override just the provider:
  ```csharp
  public sealed class AuthDbContextDesignTimeFactory : DesignTimeFactoryBase<AuthDbContext>
  {
      protected override void ConfigureProvider(DbContextOptionsBuilder<AuthDbContext> b, string cs) => b.UseSqlServer(cs);
  }
  ```

---

## 5.10 Variant: Redis.OM document store (Journals)

```csharp
// src/Services/Journals/Journals.Domain/Journals/Journal.cs
[Document(StorageType = StorageType.Json, Prefixes = new[] { nameof(Journal) })]
public partial class Journal : Entity                      // Blocks.Redis.Entity: [RedisIdField][Indexed] int Id
{
    [Indexed] public required string Abbreviation { get; set; }

    private string _name = null!;
    [Searchable] public required string Name { get => _name; set { _name = value; NormalizedName = _name.ToLowerInvariant(); } }
    [Indexed(Sortable = true)] public required string NormalizedName { get; set; }   // case-insensitive sort/search

    [Searchable] public required string Description { get; set; }
    [Indexed(JsonPath = "$.Name")] public List<Section> Sections { get; set; } = new();
    public int ArticlesCount { get; set; }
}
```

| Concern | How |
|---|---|
| Repository | `Blocks.Redis.Repository<T>`: `Collection`, `GetByIdOrThrowAsync`, `AddAsync`, `UpdateAsync`, `ReplaceAsync` |
| Integer IDs | Redis `INCR` on `{Type}:Id:Sequence` (`GenerateNewId`). Seeding calls `SetSequenceSeed` |
| Nested collection updates | `ReplaceAsync` = delete + insert (`//this is a workaround for Redis.OM not properly updating child collections`) |
| Indexes | Created at startup: `app.UseRedis()` → `provider.Connection.CreateIndex(typeof(Journal))` |
| Search | `collection.Raw("(@Abbreviation:{x}) \| (@Name:*x*) \| (@Description:*x*)")` in `SearchJournalsQueryHandler` |
| Trade-off | Redis.OM attributes sit on the **domain** entity (Journals.Domain references Blocks.Redis). This is accepted because the model is CRUD-shaped with no invariants worth isolating. |

---

## 5.11 Variant: PostgreSQL read model + Hasura (ArticleHub)

- **Writes** come only from consumers, through EF Core (`ArticleHubDbContext`).
- **Table and column naming**: `modelBuilder.UseEntityTypeNamesAsTables(new SnakeCaseNameRewriter(CultureInfo.InvariantCulture))`. snake_case is used **only here**, because that's Postgres's convention. SQL Server services keep PascalCase.
- **Reads** go through Hasura's GraphQL API, not EF:
  ```csharp
  // src/Services/ArticleHub/ArticleHub.Persistence/ArticleGraphQLReadStore.cs
  public async Task<QueryResult<ArticleDto>> GetArticlesAsync(object filter, int limit = 20, int offset = 0, CancellationToken ct = default)
  {
      var req = new GraphQLRequest
      {
          OperationName = "GetArticles",
          Query = ArticleFragment + @"
              query GetArticles($filter: ArticleBoolExp, $limit: Int = 20, $offset: Int = 0) {
                  items: article(where: $filter, limit: $limit, offset: $offset) { ...ArticleDto }
              }",
          Variables = new { filter, limit, offset }
      };
      var res = await _client.SendQueryAsync<QueryResult<ArticleDto>>(req, ct);
      if (res.Errors?.Length > 0)
          throw new ValidationException("GraphQL error", res.Errors.Select(e => new ValidationFailure("GraphQL", e.Message)));
      return res.Data ?? new QueryResult<ArticleDto>(new());
  }
  ```
- `HasuraMetadataInitService` (a `BackgroundService`) tracks all tables and relationships at startup, with up to 5 attempts while Hasura comes up.
- The search endpoint forwards the client's filter object directly as a Hasura `where` expression (`POST /api/articles/graphql`), and `Pagination` caps `limit` at 100.

---

## 5.12 Replicating persistence: summary

1. Copy `Blocks.EntityFrameworkCore` as it is.
2. `{Svc}DbContext : ApplicationDbContext<{Svc}DbContext>`, with `ApplyConfigurationsFromAssembly` + `UseEntityTypeNamesAsTables` + `UnTrackCacheableEntities`.
3. Register a scoped `DbConnection`, interceptors from DI, `TransactionProvider`, `Repository<>`, and `AddDerivedTypesOf(typeof(Repository<>))`.
4. One configuration class per entity, derived from the ladder. Use `MaxLength.*`, string enums, and complex properties for value objects.
5. Aggregate repositories override `Query()` with the includes that define the aggregate.
6. Pick the interceptor: post-save by default, transactional only when event handlers must write atomically.
7. Put master data in `Data/Master/*.json` (goes into migrations) and demo data in `Data/Test/*.json` (Development startup).
8. Mark small reference tables `ICacheable`, give them a `CachedRepository`, and warm them in a hosted service.
