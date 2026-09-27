# 07 · Testing: what exists and what the design makes testable

> The brief asked for test organization, naming, and mock patterns. This section reports **what the repository actually contains**. It doesn't invent a strategy the code doesn't have.

---

## 7.1 Current state (verified)

| Kind | Present? | Evidence |
|---|---|---|
| Unit tests | **No** | No `*Test*.csproj` and no xUnit/NUnit/MSTest reference anywhere under `src/` |
| Integration tests | **No** | same |
| End-to-end / contract tests | **No** | same |
| BDD specs | Placeholder only | `docs/gherkin/Review/AssignEditor.feature` is empty |
| Manual API tests | **Yes** | [`postman/ArticlesAPI.postman_collection.json`](../../postman/ArticlesAPI.postman_collection.json) + [`Demo.postman_environment.json`](../../postman/Demo.postman_environment.json) |
| Dev seed data | **Yes** | `Data/Test/*.json` per service, loaded at startup in Development |
| Sample files for upload flows | **Yes** | [`data/article-files/`](../../data/article-files) (manuscripts, figures, datasheets, review reports) |
| Interactive API docs | **Yes** | Swagger UI on every service (`launchUrl: "swagger"`) |

The repo's own assessment ([`docs/architecture/assessment.md` §7](../architecture/assessment.md)) grades testing **F** and records the owner's decision: generate tests *on demand* for a class before refactoring it, rather than maintain a standing suite.

**Don't copy this for a production project.** The rest of the design is very testable (§7.3). The missing tests are a gap in this reference repo, not a design choice to reuse.

---

## 7.2 How the manual verification is organized

The Postman collection follows the **business lifecycle**, one folder per service in pipeline order, so running it top to bottom walks an article through its whole life:

```
[Auth & Users]   Create User-Admin / -Auth / -CorAuth / -Editor / -Reviewer → Set First Password → Login → Me
[Journals]       Create Journal → Update → Create Section → Get / Search
[Submission]     Create Article (×4 samples) → Assign / Create&Assign Author → Upload Manuscript / Figure / DataSheet
                 → Submit → Approve | Reject → Download
[Review]         Get Article → Invite Reviewer (existing reviewer / existing user / new user) → Accept | Decline Invitation
                 → Assign Editor → Upload Review Report ×2 → Accept Article
[ArticleHub]     Search Articles (GraphQL filter) → Get Article → raw Hasura GraphQL
[Production]     (coming soon)
```

Base URLs are environment variables (`{{submission_baseUrl}}` …), so the same collection runs against local and Docker ports.

**Replicate:** keep one collection per system with **one folder per service, in lifecycle order**, and add a request for every new endpoint in the same change ([03 §3.7](03-feature-slices.md#37-replicating-a-slice-summary)). Pair it with `Data/Test` seed files so the collection has known IDs to work with.

---

## 7.3 Test seams the design creates

These are properties of the existing code that make it easy to test. They're listed so a test suite, when added, follows the architecture instead of fighting it.

| Seam | Why it's easy | What you'd test |
|---|---|---|
| **Aggregate behavior methods** | Plain C#: no DB, no DI, no HTTP. Rules throw `DomainException`; outcomes are state + `DomainEvents` | `Article.Submit` without mandatory areas throws; `AssignAuthor` twice throws; `CreateAsset` over `MaxAssetCount` throws; `ReviewInvitation.Accept` after expiry throws; `AssignTypesetter` twice throws `TypesetterAlreadyAssignedException`; each method raises the expected event |
| **State machine as a delegate** | `ArticleStateMachineFactory` is a delegate, so a test passes `stage => new FakeMachine(...)` or builds the real `ArticleStateMachine` from a list of transitions | Every row of `Data/Master/ArticleStageTransition.json` is allowed, and everything else is rejected |
| **Commands are records with the action built in** | `new SubmitArticleCommand { ... }` *is* a valid `IArticleAction<ArticleActionType>` for domain calls (set `CreatedById`) | Pass commands straight into aggregate methods |
| **Value objects** | Static factories with guards | `EmailAddress.Create("bad")` throws; `AssetNumber.Create` numbering rules; `InvitationToken.CreateNew()` is URL-safe |
| **Validators** | FluentValidation classes with no dependencies (except upload validators, which take a cached repository) | Each rule and message |
| **Mapster configs** | `TypeAdapterConfig` can be scanned into an isolated config | `Article` → `ArticleDto` produces the integration contract with polymorphic actors |
| **Handlers** | Dependencies are concrete repositories over a `DbContext`. There are **no repository interfaces to mock**, by design | Run against a real database. The no-interfaces rule means handler tests are integration tests. |
| **Consumers** | Same as handlers, plus `IFileService` (an interface, easy to fake) | Idempotency variant, compensation on failure (fake `IFileService` records `TryDeleteAsync` calls) |

**Highest-value targets**, named by the repo's own assessment: the three data-driven state machines, invitation status and expiry, the typesetter invariant, the asset ceiling, token composition (Auth), the shared `Guard`s, and `Entity.GetHashCode` (it hashes `Id`, which changes on first save).

---

## 7.4 Mocking approach implied by the architecture

The code sets two constraints on mocking:

1. **Fake only real abstractions**: the ones with an interface because a second implementation exists or the dependency crosses a process boundary. That's `IFileService`, `IEmailService` (the repo already ships `EmptyEmailService` as a no-op), gRPC clients (`IPersonService`, `IJournalService`), `IDomainEventPublisher`, `IClaimsProvider`, and the factory delegates.
2. **Don't mock persistence.** Repositories are concrete by rule, so anything that touches them is tested with a database. Anything that doesn't touch them (the domain) needs no mocks.
