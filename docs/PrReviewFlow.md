### PR Review Flow

This document maps the end-to-end pull-request review flow and the key orchestration points in the solution.

#### 1. Entry points (Azure Functions)

1. **HTTP trigger** – `ReviewPullRequestFunction` (`AiPrReview/Functions/ReviewPullRequestFunction.cs`)
   - Accepts a POST request with `ReviewRequest { repoName, pullRequestId, provider }`.
   - Parses and validates the request.
   - Estimates the pull request size (number of changed files) using the configured SCM provider.
   - Decides whether to:
     - Execute the review inline (small PRs), or
     - Enqueue a background job to process later (large PRs).
   - Returns an HTTP response indicating `completed` or `queued` status.

2. **Queue trigger** – `ReviewPullRequestQueueFunction` (`AiPrReview/Functions/ReviewPullRequestQueueFunction.cs`)
   - Listens on the `pr-reviews` Azure Storage queue.
   - Receives `PrReviewQueueMessage { RepositoryId, PullRequestId, Provider, EnqueuedAt }`.
   - Delegates execution to the application-layer orchestrator.

#### 2. Application-layer orchestration

1. **Main orchestrator** – `ReviewOrchestrator` (`AiPrReview.Application/Services/ReviewOrchestrator.cs`)
   - **Inputs**: `repoName`, `pullRequestId`, `provider` string.
   - **Responsibilities**:
     1. Resolve `ProviderType` from the provider string.
     2. Load `ProviderConfig` for the SCM provider via `IConfigurationService`.
     3. Resolve the active LLM provider and its settings via `IConfigurationService`.
     4. Create an `IRepositoryProvider` instance for the SCM (`RepositoryProviderFactory`).
     5. Fetch PR metadata and changes from the SCM provider:
        - `GetPullRequestInfoAsync` → `PullRequestInfo`
        - `GetPullRequestChangesAsync` → `IReadOnlyList<ChangedFile>`
     6. Create an `ILlmClient` instance for the chosen LLM (`LlmClientFactory`).
     7. Delegate review generation to `IReviewStrategyExecutor` based on `ProviderConfig.ReviewStrategy`.
     8. Publish resulting `ReviewComment` objects back to the SCM using `IRepositoryProvider.PublishReviewCommentsAsync`, honoring `ProviderConfig.PublishMode`.
   - **Orchestration boundaries**:
     - All cross-cutting infrastructure (SCM, LLM, persistence) is accessed **through interfaces and factories** owned by Application/Core.
     - Errors are logged and surfaced back to the calling function.

2. **Configuration service** – `ConfigurationService` (`AiPrReview.Application/Services/ConfigurationService.cs`)
   - Wraps access to:
     - `IProviderConfigRepository` (per-SCM/provider configuration).
     - `ILlmProviderRepository` (available LLM providers and settings).
     - `IConfigurationCache` (caching layer).
   - Key methods:
     - `GetProviderConfigAsync(ProviderType provider)` → `ProviderConfig`
     - `GetActiveLlmProviderAsync()` → `(LlmProvider Provider, IDictionary<string, string> Settings)`
   - Throws domain-specific exceptions such as `ProviderNotFoundException` and `NoActiveLlmProviderException` consumed by orchestration/entry points.

3. **Review strategy executor** – `ReviewStrategyExecutor` (`AiPrReview.Application/Services/ReviewStrategyExecutor.cs`)
   - **Inputs**: `ReviewStrategy`, `PullRequestInfo`, `ProviderConfig`, `IReadOnlyList<ChangedFile>`, `ILlmClient`, `CancellationToken`.
   - Uses:
     - `IPromptBuilder` to turn PR metadata and file changes into prompts.
     - `IAiResponseParser` to convert raw LLM output into structured `ReviewComment` objects.
   - Supports:
     - `ReviewStrategy.Batch` – builds a single batch prompt over multiple files.
     - `ReviewStrategy.FileLevel` – builds individual prompts per file, possibly in parallel.

4. **Prompt building and response parsing**
   - `PromptBuilder` – builds:
     - Batch prompts (`BuildBatchPrompt`).
     - Per-file prompts (`BuildFilePrompt`).
   - `AiResponseParser` – parses LLM markdown/text into:
     - `ReviewComment { FilePath, Issue, Suggestion }`.

#### 3. Core domain model

- **Entities**:
  - `ProviderConfig` – persisted configuration for SCM providers (API URL, access tokens, review strategy, publish mode, batching/concurrency limits).
  - `LlmProvider`, `LlmProviderSetting` – LLM providers and key/value settings.
- **DTOs**:
  - `PullRequestInfo` – core PR metadata (title, description, repository, SHAs).
  - `ChangedFile` – per-file change information (path, status, diff, optional full content).
  - `ReviewComment` – structured comment used to publish back to SCM.
- **Enums**:
  - `ProviderType` – `GitHub`, `AzureDevOps`.
  - `ReviewStrategy` – `Batch`, `FileLevel`.
  - `PublishMode` – `Single`, `Multiple`.
- **Interfaces**:
  - `IRepositoryProvider` – abstraction over SCM-specific operations.
  - `ILlmClient` – abstraction over provider-specific LLM APIs.
  - `IProviderConfigRepository`, `ILlmProviderRepository` – storage abstractions for configuration and LLM providers.

#### 4. Infrastructure integrations

1. **SCM providers** (`AiPrReview.Infrastructure/RepositoryProviders`)
   - `GitHubRepositoryProvider` – uses `Octokit` to:
     - Load PR metadata and files.
     - Construct `PullRequestInfo` and `ChangedFile` objects.
     - Publish review comments as GitHub PR review comments.
   - `AzureDevOpsRepositoryProvider` – uses `HttpClient` and Azure DevOps REST APIs to:
     - Query PR metadata and diff information.
     - Build diffs with `DiffPlex` into unified diff strings.
     - Publish review comments either as a single summary thread or multiple file-pinned threads.

2. **LLM clients** (`AiPrReview.Infrastructure/LlmClients`)
   - `OpenAIClient`, `ClaudeClient`, `GroqClient` – provider-specific `ILlmClient` implementations using:
     - `HttpClient` for HTTP calls.
     - `Polly` retry policies from `RetryPolicies.GetDefaultRetryPolicy()`.
   - All receive text prompts from Application and return raw responses as strings.

3. **Factories and policies**
   - `RepositoryProviderFactory` – builds an appropriate `IRepositoryProvider` instance from `ProviderConfig` and repository identifier.
   - `LlmClientFactory` – builds an `ILlmClient` from active LLM provider name and settings.
   - `RetryPolicies` – centralized definition of async retry behavior used by LLM clients (and can be reused elsewhere).

#### 5. High-level orchestration summary

1. **Request arrives** via HTTP or queue → Azure Function entry point.
2. **Configuration and providers resolved** via `ConfigurationService` and factories.
3. **PR metadata and changes loaded** by an `IRepositoryProvider`.
4. **Prompts built and sent to LLM** via `ReviewStrategyExecutor`, `IPromptBuilder`, and an `ILlmClient`.
5. **AI responses parsed** into `ReviewComment` objects via `IAiResponseParser`.
6. **Comments published back to SCM** via `IRepositoryProvider.PublishReviewCommentsAsync`.

This flow and the orchestration points above should be preserved as behavior-invariants during refactors.

