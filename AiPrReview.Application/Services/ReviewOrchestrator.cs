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

    public async Task ExecuteReviewAsync(string repositoryId, int pullRequestId)
    {
        try
        {
            _logger.LogInformation("Starting review for repository {RepoId}, PR {PrId}", repositoryId, pullRequestId);

            // 1. Load repository config
            var repoConfig = await _configService.GetRepositoryConfigByNameAsync(repositoryId);
            if (repoConfig == null || !repoConfig.IsActive)
                throw new RepositoryNotFoundException(repositoryId);

            // 2. Get active AI provider (with fallback)
            var (llmProvider, llmSettings) = await _configService.GetActiveLlmProviderAsync();

            // 3. Load review config
            var reviewConfig = await _configService.GetReviewConfigAsync(repoConfig.Id)
                               ?? new ReviewConfig { ReviewStrategy = ReviewStrategy.Batch };

            // 4. Create repository provider using the factory interface
            var repoProvider = _repoProviderFactory.Create(repoConfig);

            // 5. Fetch PR data
            var prInfo = await repoProvider.GetPullRequestInfoAsync(repoConfig.ApiUrl, pullRequestId);

            var changes = await repoProvider.GetPullRequestChangesAsync(repoConfig.ApiUrl, pullRequestId);

            var changedFiles = changes.Select(c => new ChangedFile
            {
                FilePath = c.FilePath,
                Status = c.Status,
                Diff = c.Diff
            }).ToList();

            // 7. Create AI client using the factory interface
            var llmClient = _llmClientFactory.Create(llmProvider.Name, llmSettings);

            // 8. Execute review strategy
            var comments = await _strategyExecutor.ExecuteAsync(
                reviewConfig.ReviewStrategy,
                prInfo,
                changedFiles,
                reviewConfig,
                llmClient,
                CancellationToken.None);

            // 9. Publish comments
            if (comments.Any())
            {
                await repoProvider.PublishReviewCommentsAsync(repoConfig.ApiUrl, pullRequestId, comments);
                _logger.LogInformation("Published {Count} comments for PR {PrId}", comments.Count, pullRequestId);
            }
            else
            {
                _logger.LogInformation("No issues found for PR {PrId}", pullRequestId);
            }
        }
        catch (RepositoryNotFoundException ex)
        {
            _logger.LogError(ex, "Repository not found: {RepoId}", repositoryId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing review for repository {RepoId}, PR {PrId}", repositoryId, pullRequestId);
            throw;
        }
    }
}