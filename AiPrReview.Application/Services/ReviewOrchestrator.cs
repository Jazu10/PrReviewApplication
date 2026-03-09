using Microsoft.Extensions.Logging;
using AiPrReview.Core.Entities;
using AiPrReview.Application.Interfaces;
using AiPrReview.Application.Exceptions;
using AiPrReview.Core.Enums;
using AiPrReview.Core.Dto;
using AiPrReview.Core.Interfaces;

namespace AiPrReview.Application.Services;

    public class ReviewOrchestrator
{
    private readonly IConfigurationService _configService;
        private readonly IRepositoryProviderFactory _repoProviderFactory;
        private readonly ILlmClientFactory _llmClientFactory;
    private readonly IReviewStrategyExecutor _strategyExecutor;
    private readonly ILogger<ReviewOrchestrator> _logger;

    public ReviewOrchestrator(
        IConfigurationService configService,
        IRepositoryProviderFactory repoProviderFactory,
        ILlmClientFactory llmClientFactory,
        IReviewStrategyExecutor strategyExecutor,
        ILogger<ReviewOrchestrator> logger)
    {
        _configService = configService;
        _repoProviderFactory = repoProviderFactory;
        _llmClientFactory = llmClientFactory;
        _strategyExecutor = strategyExecutor;
        _logger = logger;
    }

    public async Task ExecuteReviewAsync(string repoName, int pullRequestId, string provider, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting review for repository {RepoName}, PR {PrId}", repoName, pullRequestId);

            var providerType = ParseProvider(provider);

            var providerConfig = await _configService.GetProviderConfigAsync(providerType);

            var (llmProvider, llmSettings) = await _configService.GetActiveLlmProviderAsync();

            var repoProvider = _repoProviderFactory.Create(providerConfig, repoName);

            var (prInfo, changedFiles) = await FetchPullRequestDataAsync(repoProvider, providerConfig.ApiUrl, pullRequestId, cancellationToken);

            var llmClient = _llmClientFactory.Create(llmProvider.Name, llmSettings);

            var comments = await GenerateReviewCommentsAsync(providerConfig, prInfo, changedFiles, llmClient, cancellationToken);

            await PublishReviewCommentsAsync(repoProvider, providerConfig, pullRequestId, comments, cancellationToken);
        }
        catch (ProviderNotFoundException ex)
        {
            _logger.LogError(ex, "Provider {Provider} not found for repository {RepoName}. Review cannot be executed.", provider, repoName);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing review for repository {RepoName}, PR {PrId}", repoName, pullRequestId);
            throw;
        }
    }

    private static ProviderType ParseProvider(string provider)
    {
        if (!Enum.TryParse<ProviderType>(provider, true, out var providerType))
        {
            throw new ArgumentException($"Invalid provider type: {provider}", nameof(provider));
        }

        return providerType;
    }

    private static async Task<(PullRequestInfo prInfo, List<ChangedFile> changedFiles)> FetchPullRequestDataAsync(
        IRepositoryProvider repoProvider,
        string apiUrl,
        int pullRequestId,
        CancellationToken cancellationToken)
    {
        var prInfo = await repoProvider.GetPullRequestInfoAsync(apiUrl, pullRequestId);
        var changes = await repoProvider.GetPullRequestChangesAsync(apiUrl, pullRequestId);

        var changedFiles = changes.Select(c => new ChangedFile
        {
            FilePath = c.FilePath,
            Status = c.Status,
            Diff = c.Diff,
            Content = c.Content
        }).ToList();

        return (prInfo, changedFiles);
    }

    private async Task<IReadOnlyList<ReviewComment>> GenerateReviewCommentsAsync(
        ProviderConfig providerConfig,
        PullRequestInfo prInfo,
        IReadOnlyList<ChangedFile> changedFiles,
        ILlmClient llmClient,
        CancellationToken cancellationToken)
    {
        return await _strategyExecutor.ExecuteAsync(
            providerConfig.ReviewStrategy,
            prInfo,
            providerConfig,
            changedFiles,
            llmClient,
            cancellationToken);
    }

    private async Task PublishReviewCommentsAsync(
        IRepositoryProvider repoProvider,
        ProviderConfig providerConfig,
        int pullRequestId,
        IReadOnlyList<ReviewComment> comments,
        CancellationToken cancellationToken)
    {
        if (!comments.Any())
        {
            _logger.LogInformation("No issues found for PR {PrId}", pullRequestId);
            return;
        }

        await repoProvider.PublishReviewCommentsAsync(providerConfig.ApiUrl, pullRequestId, comments, providerConfig.PublishMode);
        _logger.LogInformation("Published {Count} comments for PR {PrId}", comments.Count, pullRequestId);
    }
}