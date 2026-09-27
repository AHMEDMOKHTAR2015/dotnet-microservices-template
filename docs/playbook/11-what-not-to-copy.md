# 11 · What not to copy

> The repo is a **teaching reference**: several services deliberately solve one problem differently, and some things are unfinished. This page separates *intentional variation* (see [09](09-decision-framework.md)) from **accidents and gaps** you shouldn't reproduce. Every item was checked against the current source.

---

## Security

| # | Issue | Evidence | Do instead |
|---|---|---|---|
| S1 | **Real-looking secrets committed in `appsettings.json`**: an Azure Storage account key, an SMTP app password, and the JWT signing secret | `Services/Submission/Submission.API/appsettings.json` (`ConnectionStrings:AzureFileStorage`, `EmailOptions:Smtp:Password`, `JwtOptions:Secret`) | Keep placeholders in `appsettings.json`; use user-secrets locally (every API csproj already has a `<UserSecretsId>`) and environment variables in containers. Rotate any key that was ever committed. |
| S2 | **Per-article (resource) authorization never runs in the FastEndpoints services.** Production registers `ArticleAccessAuthorizationHandler` + `IArticleAccessChecker`, but its endpoints use only `[Authorize(Roles = …)]`, so `ArticleRoleRequirement` is never attached. Any typesetter can act on any article. | `Production.API/DependencyInjection.cs:60`; `grep -rn "ArticleRoleRequirement\|RequireRoleAuthorization" src/Services/Production` → nothing | If you use attribute-routed endpoints, attach the resource requirement too (a named policy containing `ArticleRoleRequirement`). The repo shows the Minimal API/Carter form only. |
| S3 | API Gateway has no authentication | `ApiGateway/Program.cs`: `//todo add authentication` | Authenticate at the edge, or document that services are the only enforcement point |
| S4 | `HttpContextProvider.GetClaimValues(string claimName)` **ignores its argument** and always reads role claims | `Blocks.AspNetCore/HttpContextProvider.cs`: `=> _httpContextAccessor.GetClaimValues(ClaimTypes.Role)` | Pass `claimName` through. It's harmless today only because the one caller asks for roles. |

## Reliability and correctness

| # | Issue | Evidence | Do instead |
|---|---|---|---|
| R1 | **No transactional outbox.** Integration events are published from a post-commit domain-event handler; if the publish fails, the state change is committed and the event is lost. | `DispatchDomainEventsInterceptor.SavedChangesAsync` → `PublishIntegrationEventOn*Handler` | Add an outbox (MassTransit's EF outbox or your own) before relying on events for business-critical handoffs |
| R2 | Consumer comments say *"rethrow so MassTransit redelivers"*, but **no retry policy is configured** | `Blocks.Messaging/MassTransit/DependencyInjection.cs` has no `UseMessageRetry`/`UseDelayedRedelivery` | Configure retry/redelivery explicitly in the shared `AddMassTransitWithRabbitMQ` |
| R3 | `GrpcServicesOptions:Services:*:EnableRetry` and `Retry` are set in config, but only the **unused** `AddCodeFirstGrpcClientWithRetry` reads them | `GrpcClientRegistrationExtensions.cs`; services call `AddCodeFirstGrpcClient` | Either wire the retry variant or remove the misleading config |
| R4 | gRPC servers throw `NotFoundException` with no status mapping | `JournalGrpcService.cs`: `//todo - Implement an interceptor which will transform the exception into a valid GRPC error response` | Add a server interceptor: domain/HTTP exceptions → `RpcException` with `NotFound`/`InvalidArgument` |
| R5 | `TransactionOptions: { UseSingleTransaction: true }` is bound in Submission and Review, which use the **non**-transactional interceptor, so the setting does nothing | `Submission.API/DependencyInjection.cs` `AddAndValidateOptions<TransactionOptions>`; `Submission.Persistence` registers `DispatchDomainEventsInterceptor` | Bind `TransactionOptions` only where `TransactionalDispatchDomainEventsInterceptor` is used |
| R6 | `Entity<T>.GetHashCode()` hashes `Id`, which changes from `0` to the database value on first save. A new entity put in a `HashSet`/dictionary before saving can't be found afterwards. | `Blocks.Domain/Entities/Entity.cs`; noted in `docs/architecture/assessment.md` | Don't key hashed collections on unsaved entities; or make transient entities hash by reference |
| R7 | Correlation ID stops at the service edge | only the gateway and `RequestContextMiddleware` touch `X-Correlation-ID` | Forward it as gRPC metadata and a MassTransit header |

## Consistency: pick the majority variant

| # | Inconsistency | Where | Use |
|---|---|---|---|
| C1 | `GlobalExceptionMiddleware` registered **after** `UseAuthorization` | Review, ArticleHub `Program.cs` | Register it early, before routing/auth, as in Auth, Submission, Journals ([06 §6.4](06-cross-cutting.md#64-middleware-pipeline-order)) |
| C2 | `RequestContextMiddleware`/`RequestDiagnosticsMiddleware` wired in only 2 of 6 services. Review's `LoggingBehavior` reads a `RequestContext` nothing fills, so its correlation ID is always null. | only Submission and ArticleHub | Wire both middlewares in every service that registers `RequestContext` |
| C3 | Production calls `UseAuthentication()` **before** `UseRouting()`, and registers `AddControllers`/`MapControllers` with no controllers | `Production.API/Program.cs`, `DependencyInjection.cs` | Routing → authentication → authorization; drop unused MVC registration |
| C4 | Side effect sent inline from a command handler while the aggregate raises an event nothing handles | `InviteReviewerCommandHandler` sends the email; `ReviewerInvited` has no handler | Send from `SendInvitationEmailOnReviewerInvitedHandler`, like the other emails |
| C5 | Two validation message vocabularies | MediatR services: `Blocks.Core/FluentValidation` extensions; Production: local `ValidatorsMessagesConstants` | One shared set of message extensions for the whole project |
| C6 | Query typed as a command | `DownloadFileQuery : ICommand<DownloadFileResponse>`, handled by `DownloadFileCommandHandler` | Reads implement `IQuery<T>`; handler `{Query}Handler` |
| C7 | Domain factories take **wire contracts** (`ArticleDto`, `PersonInfo`), so Submission/Review/Production Domain projects reference contract packages | `Article.FromSubmission(ArticleDto …)`, `Author.Create(PersonInfo …)`; Domain csproj → `Articles.Grpc.Contracts`/`Articles.IntegrationEvents.Contracts` | Define a creation-info interface in the domain or shared kernel and let the contract implement it, as Auth does with `IPersonCreationInfo` ([02 §2.3](02-domain-modeling.md#23-factories-four-kinds)); or map in the consumer |
| C8 | Manual `ApplyConfiguration(new …)` list with `//todo use ApplyConfigurationsFromAssembly`; repositories registered one by one | `ProductionDbContext.OnModelCreating`, `Production.Persistence/DependencyInjection.cs` | `ApplyConfigurationsFromAssembly` + `AddDerivedTypesOf(typeof(Repository<>))` |
| C9 | `SetStage` validates **before** the "no change" early return in Submission, **after** it in Review; Production's `SetStage` doesn't validate at all (`AssignTypesetter` checks first; the upload endpoints rely on the asset-level machine) | `Behaviors/Article.cs` in each service | Validate first, always, inside `SetStage` (the Submission form) |
| C10 | Domain folder layout differs (by type vs by aggregate) | Submission vs the rest | By aggregate ([02 §2.11](02-domain-modeling.md#211-organizing-the-domain-project)) |
| C11 | Primary-constructor parameter naming varies (`dbContext` vs `_dbContext`), even between sibling repositories | `Production.Persistence/Repositories` | Pick one rule per project ([08 §8.3](08-style-guide.md#83-members-and-variables)) |
| C12 | A consumer throws `BadRequestException` (an HTTP type) for a duplicate message | `ArticleHub/.../ArticleApprovedForReviewConsumer.cs` | In consumers, prefer skip-if-exists, or throw a domain/infrastructure exception |
| C13 | `DomainException` thrown **outside the Domain**: the "open invitation already exists" rule lives in a handler, and a consumer throws it for an unknown role | `Review.Application/Features/Invitations/InviteReviewer/InviteReviewerCommandHandler.cs:29`, `…/InitializeFromSubmission/ArticleApprovedForReviewConsumer.cs:144` | Move the rule into the aggregate (`Article.CreateInvitation` already checks unexpired invitations by email); application-level failures throw `BadRequestException` |

## Dead code and leftovers

| # | Item | Evidence | Do instead |
|---|---|---|---|
| D1 | AutoMapper profiles and package references, but AutoMapper is **never registered** | `Auth.API/.../CreateUserCommandMapping.cs`, `Production.API/Features/_Shared/FileResponseMappingProfile.cs`; `//.AddAutoMapper(...)` commented out | One mapper (Mapster). Delete the profiles and packages. |
| D2 | Multitenancy stack nobody uses; `TenantRepositoryBase` throws an `HttpException` from the persistence layer | `Blocks.EntityFrameworkCore/TenantDbContext.cs` (internal), `TenantRepositoryBase.cs` | Leave it out until needed; persistence throws `NotFoundException` via guards, not raw `HttpException` |
| D3 | Duplicate reference-data cache in a repository | `Submission.Persistence/Repositories/AssetRepository.GetAssetTypes()` duplicates `AssetTypeRepository` (`CachedRepository`) | One `CachedRepository` per `ICacheable` type |
| D4 | `FileStorage.MinIO` is referenced by no service and marked `// todo - not tested`; `EmailService.SendGrid` is referenced by no service | `src/Modules/FileService/FileStorage.MinIO`, `docs/tech-debt/global.md` glb-2/glb-3 | Ship providers you actually run and test |
| D5 | Commented-out code blocks and a fully commented-out `Invitee` value object | e.g. `Review.Domain/Invitations/ValueObjects/Invitee.cs`, the authorization block in `ArticleHub.API/DependencyInjection.cs` | Delete, or turn into an `//insight` explaining the rejected option (as `ArticleHubDbContext.OnConfiguring` does) |
| D6 | Stateless kept despite the author's own `//todo - reimplement it directly with the database (cached), remove stateless library` | `Submission.Application/StateMachines/ArticleStateMachine.cs` | The *seam* (interface + delegate + guard) is the pattern. The implementation behind it can be a plain lookup over the cached table. |

## Naming slips

| Slip | Where | Correct |
|---|---|---|
| `DependecyInjection` class name | `Production.API/DependencyInjection.cs` | `DependencyInjection` |
| `AddMongoFileStorageAsSingletone` | `FileService.MongoGridFS/FileStorageRegistration.cs` | `…AsSingleton` |
| `GetArticleSummaryResonse`, `DownloadFileQuerydValidator` | Production, Submission | spell-check public names |
| Folder `Articles.Integration.Contracts` vs namespace/assembly `Articles.IntegrationEvents.Contracts`; `ArticlePublishedEvent` in namespace `Articles.Abstractions.Events` | BuildingBlocks | Folder = assembly = namespace root |
| Folders `FileService.*` vs assemblies `FileStorage.*` | `Modules/FileService/FileService.Contracts/FileStorage.Contracts.csproj` | One name for the capability |

## Missing entirely

| Item | Note |
|---|---|
| Automated tests | See [07](07-testing.md). The design supports them; the repo just doesn't have them. |
| API Gateway usable routes | No path transforms (YARP forwards `/submission/api/...` unchanged), no Auth/Journals routes; the Postman "API Gateway" folder is empty |
| Production in docker-compose | `production-api` is commented out |
