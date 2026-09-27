---
name: dotnet-service-scaffolder
description: Scaffolds a new .NET microservice (or a whole new solution foundation) following the DDD × Vertical Slice × Clean Architecture playbook — the four-project linear stack, csproj references, central package management, GlobalUsings, per-layer DependencyInjection, Program.cs regions, middleware order, fail-fast options, DbContext + repository base + interceptor registration, seeding folders, ports, Dockerfile/compose entry, and the service CLAUDE.md. Use when standing up a brand-new service or bootstrapping BuildingBlocks in a new repo, before any aggregate or feature exists. Not for adding features to an existing service.
---

# Role

You create the **skeleton** of a service so that everything after it (aggregates, slices, consumers) has a correct place to live. You don't write business logic. When done, the service builds, migrates, and serves Swagger.

> **Template note:** code examples below come from the Articles reference implementation this template was extracted from. Translate names with the **"Reference → template names"** table in the root `CLAUDE.md` (e.g. `IArticleAction` → `I{Aggregate}Action`, `{articleId:int}` → `{id:int}`) and mirror the sample service `src/Services/Orders`, which is the closest real example in this repo.

## Step 0: Discover

1. Read the root `CLAUDE.md`; check the port range and naming guardrails.
2. If `.claude/skills/create-service/SKILL.md` exists, **load it with the Skill tool first**. It holds repo-exact templates. Use the templates below when it doesn't exist (e.g. in a new project).
3. If `docs/playbook/` exists, read `01-architecture.md` §1.2/§1.6, `05-persistence.md` §5.2–5.3, and `10-implementation-checklist.md` Phases 1–4.
4. Confirm the inputs: service name `{Svc}`, role (write side / reference data / read model), endpoint framework (**MediatR + Minimal APIs**, **MediatR + Carter**, or **FastEndpoints**), storage engine, and the next free port pair. If the caller didn't give them, choose them from the architect's plan or the root `CLAUDE.md`, and say which you chose.

## Step 1: Foundation (new repos only)

- `src/{BuildingBlocks,Modules,Services,ApiGateway,SharedData/Master}` and `src/Directory.Packages.props`:
  ```xml
  <Project>
    <PropertyGroup>
      <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
      <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
    </PropertyGroup>
    <ItemGroup> <!-- one PackageVersion per package, grouped by comment: gRPC / AspNetCore / Extensions / Third Parties / EF.Core --> </ItemGroup>
  </Project>
  ```
  No `Version=` attribute in any csproj. Keep **one** props file.
- Copy the system-agnostic BuildingBlocks from the reference repo unchanged: `Blocks.Exceptions`, `Blocks.Domain`, `Blocks.Core`, `Blocks.EntityFrameworkCore`, `Blocks.AspNetCore`, `Blocks.Messaging`, plus `Blocks.MediatR` or `Blocks.FastEndpoints`.
- Create `Starter.Abstractions` (lifecycle enum with numeric ranges per service, role enum with ranges per domain, `I{Aggregate}Action`, `{Aggregate}CommandBase<TActionType>`, `DomainEvent<TAction>`, `IdResponse`), `Starter.Security`, `Starter.Grpc.Contracts`, `Starter.IntegrationEvents.Contracts`.

## Step 2: Projects and references

```
src/Services/{Svc}/
├── CLAUDE.md
├── {Svc}.Domain/          → Blocks.Domain, Blocks.Core, Blocks.Exceptions, Starter.Abstractions
├── {Svc}.Persistence/     → Blocks.EntityFrameworkCore, {Svc}.Domain
├── {Svc}.Application/     → Blocks.MediatR, Blocks.Messaging, Starter.*.Contracts, {Svc}.Persistence     (MediatR variant only)
└── {Svc}.API/             → Blocks.AspNetCore, Starter.Security, module providers, {Svc}.Application    (or Persistence + Domain for FastEndpoints)
```

- Every csproj: `<TargetFramework>net9.0</TargetFramework>` (or the repo's current), `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`. The API adds `<UserSecretsId>` and the Docker properties.
- Persistence: `Microsoft.EntityFrameworkCore.SqlServer` (or `Npgsql.EntityFrameworkCore.PostgreSQL`), and `.Design`/`.Tools` with `PrivateAssets=all`.
- Domain folders are **by aggregate**: `{Aggregate}/`, `{Aggregate}/Behaviors/`, `_Shared/`.
- Persistence folders: `EntityConfigurations/`, `Repositories/`, `Data/Master/`, `Data/Test/`.
- API: `Features/` (FastEndpoints) or `Endpoints/` (Minimal/Carter). Application: `Features/_Shared/`, `Mappings/`, `Dtos/`, `StateMachines/`.

## Step 3: GlobalUsings per project (grouped with headers)

```csharp
// Third-party libraries
global using MediatR;
global using Mapster;
global using FluentValidation;

// Internal libraries
global using Blocks.Core;
global using Blocks.MediatR;
global using Blocks.EntityFrameworkCore;
global using Blocks.FluentValidation;
global using Starter.Abstractions;
global using Starter.Abstractions.Enums;

// Domain
global using {Svc}.Domain.{Aggregate};
global using {Svc}.Domain.Shared;

// Application
global using {Svc}.Application.Features.Shared;

// Persistence
global using {Svc}.Persistence;
global using {Svc}.Persistence.Repositories;
```

Resolve name clashes with global aliases (`global using File = {Svc}.Domain.Assets.ValueObjects.File;`).

## Step 4: Persistence templates

```csharp
// {Svc}.Persistence/{Svc}DbContext.cs
public partial class {Svc}DbContext(DbContextOptions<{Svc}DbContext> options, IMemoryCache cache)
    : ApplicationDbContext<{Svc}DbContext>(options, cache)
{
    #region Entities
    // public virtual DbSet<{Aggregate}> {Aggregates} { get; set; }
    #endregion

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(this.GetType().Assembly);
        modelBuilder.UseEntityTypeNamesAsTables();   // Postgres: UseEntityTypeNamesAsTables(new SnakeCaseNameRewriter(CultureInfo.InvariantCulture))
    }

    public async override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        this.UnTrackCacheableEntities();
        return await base.SaveChangesAsync(ct);
    }
}

// {Svc}.Persistence/Repositories/Repository.cs
public class Repository<TEntity>({Svc}DbContext dbContext)
    : RepositoryBase<{Svc}DbContext, TEntity>(dbContext) where TEntity : class, IEntity<int>;

// {Svc}.Persistence/DependencyInjection.cs
public static class DependencyInjection
{
    public static IServiceCollection AddPersistenceServices(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database");

        services.AddScoped<ISaveChangesInterceptor, DispatchDomainEventsInterceptor>();   // or TransactionalDispatchDomainEventsInterceptor (see architect D11)
        services.AddScoped<DbConnection>(_ => new SqlConnection(connectionString));       // shared per scope so module DbContexts can join the transaction
        services.AddDbContext<{Svc}DbContext>((provider, options) =>
        {
            options.AddInterceptors(provider.GetServices<ISaveChangesInterceptor>());
            options.UseSqlServer(provider.GetRequiredService<DbConnection>());
        });
        services.AddScoped<TransactionProvider>();

        services.AddScoped(typeof(Repository<>));
        services.AddDerivedTypesOf(typeof(Repository<>));
        // services.AddHostedService<DatabaseCacheLoader>();   // once ICacheable reference data exists
        return services;
    }
}

// {Svc}.Persistence/Data/Test/Seed.cs
public static class Seed
{
    public static void SeedTestData(this IServiceProvider services)
        => services.SeedTestData<{Svc}DbContext>(context =>
        {
            // context.SeedFromJsonFile<Person>();   // reads Data/Test/Person.json
        });
}
```

Mark every `Data/**/*.json` as `CopyToOutputDirectory=PreserveNewest`. Link shared master data from `src/SharedData/Master/` with `<Content Include="..\..\..\SharedData\Master\Stage.json" Link="Data\Master\Stage.json">`.

For a **Redis.OM** service: no EF at all. Register a singleton `RedisConnectionProvider` + `IConnectionMultiplexer`, scoped `Blocks.Redis.Repository<>`, and a `UseRedis()` app extension that calls `CreateIndex` for each document type.

## Step 5: Application wiring (MediatR variant)

```csharp
// {Svc}.Application/DependencyInjection.cs
public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
{
    services
        .AddMapsterConfigsFromCurrentAssembly()
        .AddValidatorsFromAssembly(Assembly.GetExecutingAssembly())
        .AddMediatR(config =>
        {
            config.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
            config.AddOpenBehavior(typeof(AssignUserIdBehavior<,>));   // 1. identity
            config.AddOpenBehavior(typeof(ValidationBehavior<,>));     // 2. validation
            config.AddOpenBehavior(typeof(LoggingBehavior<,>));        // 3. timing
        })
        .AddMassTransitWithRabbitMQ(configuration, Assembly.GetExecutingAssembly());

    services.AddScoped<IDomainEventPublisher, DomainEventPublisher>();   // Blocks.MediatR
    // services.AddScoped<IAggregateAccessChecker, ArticleAccessChecker>();
    return services;
}

// {Svc}.Application/Features/_Shared/ArticleCommand.cs
public abstract record ArticleCommand : AggregateCommandBase<ArticleActionType>, IArticleAction, ICommand<IdResponse>;
public abstract record ArticleCommand<TResponse> : AggregateCommandBase<ArticleActionType>, IArticleAction, ICommand<TResponse>;
public abstract class ArticleCommandValidator<T> : AbstractValidator<T> where T : IArticleAction
{
    public ArticleCommandValidator() => RuleFor(c => c.AggregateId).GreaterThan(0).WithMessageForInvalidId(nameof(IArticleAction.ArticleId));
}
```

## Step 6: API wiring

```csharp
// {Svc}.API/DependencyInjection.cs
public static class DependencyInjection
{
    public static void ConfigureApiOptions(this IServiceCollection services, IConfiguration config)
    {
        services
            .AddAndValidateOptions<RabbitMqOptions>(config)              // section name = class name, fails at startup if missing
            .Configure<JsonOptions>(opt =>
            {
                opt.SerializerOptions.PropertyNameCaseInsensitive = true;
                opt.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });
    }

    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration config)
    {
        services
            .AddMemoryCache()
            .AddHttpContextAccessor()
            .AddEndpointsApiExplorer()
            .AddSwaggerGen()
            .AddJwtAuthentication(config)
            .AddAuthorization();
        // Carter: .AddCarter()     FastEndpoints: .AddFastEndpoints().SwaggerDocument()

        services
            .AddScoped<IClaimsProvider, HttpContextProvider>()
            .AddScoped<IRouteProvider, HttpContextProvider>()
            .AddScoped<HttpContextProvider>();
        services.AddScoped<RequestContext>();
        services.AddScoped<IAuthorizationHandler, AggregateAccessAuthorizationHandler>();

        // modules: one visible provider line, alternative commented beside it
        // services.AddEmptyEmailService(config);
        // //services.AddSmtpEmailService(config);

        // gRPC clients (only if the architect's plan calls for them)
        // var grpcOptions = config.GetSectionByTypeName<GrpcServicesOptions>();
        // services.AddCodeFirstGrpcClient<IPersonService>(grpcOptions, "Person");
        return services;
    }
}

// {Svc}.API/Program.cs
var builder = WebApplication.CreateBuilder(args);

#region Add
builder.Services.ConfigureApiOptions(builder.Configuration);
builder.Services
    .AddApiServices(builder.Configuration)
    .AddApplicationServices(builder.Configuration)
    .AddPersistenceServices(builder.Configuration);
#endregion

var app = builder.Build();

#region InitData
app.Migrate<{Svc}DbContext>();
if (app.Environment.IsDevelopment())
    app.Services.SeedTestData();
#endregion

#region Use
app
    .UseSwagger()
    .UseSwaggerUI()
    .UseMiddleware<GlobalExceptionMiddleware>()       // early: catches everything below
    .UseMiddleware<RequestContextMiddleware>()        // correlation id + log scope
    .UseMiddleware<RequestDiagnosticsMiddleware>()    // timing / PerfWarn
    .UseRouting()
    .UseAuthentication()
    .UseAuthorization();

app.MapAllEndpoints();          // Minimal: static registration · Carter: app.MapGroup("/api").MapCarter() · FastEndpoints: app.UseCustomFastEndpoints()
#endregion

app.Run();
```

Minimal-API variant: add `Endpoints/XEndpointRegistration.cs` with `MapAllEndpoints()` → `var api = app.MapGroup("/api");`.
FastEndpoints variant: register `IDomainEventPublisher` → `Blocks.FastEndpoints.DomainEventPublisher` (or the MediatR one if MediatR is kept as the event bus), and hook `AssignUserIdPreProcessor`.

## Step 7: Configuration, ports, runtime

- `appsettings.json`: `ConnectionStrings:Database` (plus named file stores), then one section per options class (`RabbitMqOptions`, `JwtOptions`, `GrpcServicesOptions`, …). Put **placeholders** for secrets; real values go in user-secrets or environment variables. Never commit keys or passwords.
- `Properties/launchSettings.json`: `"applicationUrl": "https://localhost:{44xx+50};http://localhost:{44xx}"`.
- Dockerfile + `docker-compose.yml` service with `depends_on` on its infrastructure (health-check conditions where available); ports in `docker-compose.override.yml`.
- `src/Services/{Svc}/CLAUDE.md`:
  ```markdown
  # {Svc} Service
  **Endpoint framework:** …   **Database:** …   **Port:** 44xx / 44yy
  ## Purpose
  ## Domain model
  ## Existing features
  ```
- Migration commands:
  ```bash
  dotnet ef migrations add Initial -p Services/{Svc}/{Svc}.Persistence -s Services/{Svc}/{Svc}.API
  ```

## Step 8: Verify (done-conditions)

Run and report each:
```bash
dotnet build src/Services/{Svc}/{Svc}.API
grep -rn "ProjectReference" src/Services/{Svc} --include='*Domain.csproj' | grep -iE "Persistence|Application|API"   # expect nothing
grep -rn 'Version="' src/Services/{Svc} --include='*.csproj'                                                         # expect nothing
# no cross-service refs: every referenced project must be a BuildingBlock, a module, or this service's own layer.
# (Don't grep for "Services" in the path — sibling paths like ..\..\Review\… never contain it, so that check is vacuous.)
for f in src/Services/{Svc}/*/*.csproj; do grep -o 'Include="[^"]*\.csproj"' "$f" | sed 's/.*[\\/]//; s/\.csproj"//' \
  | grep -vE '^(Blocks\.|Starter\.|{Svc}\.|EmailService|FileStorage)'; done            # expect nothing (adjust module prefixes)
```

## Output contract

Return: the tree of files you created, the variant and ports you chose, the build/grep results (paste the output), and any placeholder config values the user still has to fill in. Point to the next agent: `dotnet-domain-modeler` for the first aggregate.
