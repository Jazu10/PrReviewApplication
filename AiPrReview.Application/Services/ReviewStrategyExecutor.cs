using Microsoft.Extensions.Logging;
using AiPrReview.Core.Entities;
using AiPrReview.Core.Interfaces;
using AiPrReview.Application.Interfaces;
using System.Collections.Generic;
using AiPrReview.Core.Enums;
using AiPrReview.Core.Dto;

namespace AiPrReview.Application.Services;

public class ReviewStrategyExecutor : IReviewStrategyExecutor
{
    private readonly IPromptBuilder _promptBuilder;
    private readonly IAiResponseParser _responseParser;
    private readonly ILogger<ReviewStrategyExecutor> _logger;

    public ReviewStrategyExecutor(
        IPromptBuilder promptBuilder,
        IAiResponseParser responseParser,
        ILogger<ReviewStrategyExecutor> logger)
    {
        _promptBuilder = promptBuilder;
        _responseParser = responseParser;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ReviewComment>> ExecuteAsync(
        ReviewStrategy strategy,
        PullRequestInfo prInfo,
        ProviderConfig config,
        IReadOnlyList<ChangedFile> files,
        ILlmClient llmClient,
        CancellationToken cancellationToken)
    {
        return strategy switch
        {
            ReviewStrategy.Batch => await BatchReviewAsync(prInfo, files, config, llmClient, cancellationToken),
            ReviewStrategy.FileLevel => await FileLevelReviewAsync(prInfo, files, config, llmClient, cancellationToken),
            _ => throw new NotSupportedException($"Strategy {strategy} not supported.")
        };
    }

    private async Task<IReadOnlyList<ReviewComment>> BatchReviewAsync(
     PullRequestInfo prInfo,
     IReadOnlyList<ChangedFile> files,
     ProviderConfig config,
     ILlmClient llmClient,
     CancellationToken cancellationToken)
    {
        var allComments = new List<ReviewComment>();
        var batches = files.Chunk(config.MaxFilesPerBatch ?? files.Count);
        foreach (var batch in batches)
        {
            var prompt = _promptBuilder.BuildBatchPrompt(prInfo, batch.ToList());
            var response = await llmClient.ReviewAsync(prompt, cancellationToken);  // Direct string
            var comments = _responseParser.Parse(response);
            allComments.AddRange(comments);
        }
        return allComments;
    }

    private async Task<IReadOnlyList<ReviewComment>> FileLevelReviewAsync(
        PullRequestInfo prInfo,
        IReadOnlyList<ChangedFile> files,
        ProviderConfig config,
        ILlmClient llmClient,
        CancellationToken cancellationToken)
    {
        var allComments = new List<ReviewComment>();
        var semaphore = new SemaphoreSlim(config.MaxConcurrentFileReviews ?? 5);

        var tasks = files.Select(async file =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                _logger.LogInformation("Reviewing file {FilePath}", file.FilePath);
                var prompt = _promptBuilder.BuildFilePrompt(prInfo, file);
                var response = await llmClient.ReviewAsync(prompt, cancellationToken);  // Direct string
                return _responseParser.Parse(response);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks);
        allComments.AddRange(results.SelectMany(c => c));
        return allComments;
    }
}