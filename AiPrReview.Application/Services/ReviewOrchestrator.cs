using Microsoft.Extensions.Logging;
using AiPrReview.Core.Entities;
using AiPrReview.Application.Interfaces;
using AiPrReview.Application.Exceptions;
using AiPrReview.Core.Enums;
using AiPrReview.Core.Dto;

namespace AiPrReview.Application.Services;

public class ReviewOrchestrator
{
    private readonly IConfigurationService _configService;
    private readonly IRepositoryProviderFactory _repoProviderFactory;  // interface
    private readonly ILlmClientFactory _llmClientFactory;              // interface
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

    public async Task ExecuteReviewAsync(string repoName, int pullRequestId, string provider)
    {
        try
        {
            _logger.LogInformation("Starting review for repository {RepoName}, PR {PrId}", repoName, pullRequestId);

            // Convert the string 'provider' to the enum 'ProviderType'
            if (!Enum.TryParse<ProviderType>(provider, true, out var providerType))
            {
                throw new ArgumentException($"Invalid provider type: {provider}", nameof(provider));
            }

            // Get global provider config
            var providerConfig = await _configService.GetProviderConfigAsync(providerType);

            // 1. Get active AI provider (with fallback)
            var (llmProvider, llmSettings) = await _configService.GetActiveLlmProviderAsync();

            // 2. Create repository provider using the factory interface
            var repoProvider = _repoProviderFactory.Create(providerConfig, repoName);

            // 3. Fetch PR data
            var prInfo = await repoProvider.GetPullRequestInfoAsync(providerConfig.ApiUrl, pullRequestId);

            var changes = await repoProvider.GetPullRequestChangesAsync(providerConfig.ApiUrl, pullRequestId);

            var changedFiles = changes.Select(c => new ChangedFile
            {
                FilePath = c.FilePath,
                Status = c.Status,
                Diff = c.Diff
            }).ToList();

            // 4. Create AI client using the factory interface
            var llmClient = _llmClientFactory.Create(llmProvider.Name, llmSettings);

            // 5. Execute review strategy
            var comments = await _strategyExecutor.ExecuteAsync(
                providerConfig.ReviewStrategy,
                prInfo,
                providerConfig,
                changedFiles,
                llmClient,
                CancellationToken.None);

            // 6. Publish comments
            if (comments.Any())
            {
                await repoProvider.PublishReviewCommentsAsync(providerConfig.ApiUrl, pullRequestId, comments, providerConfig.PublishMode);
                _logger.LogInformation("Published {Count} comments for PR {PrId}", comments.Count(), pullRequestId);
            }
            else
            {
                _logger.LogInformation("No issues found for PR {PrId}", pullRequestId);
            }
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
}