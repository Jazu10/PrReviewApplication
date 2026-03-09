using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using AiPrReview.Application.Services;
using AiPrReview.Infrastructure.BackgroundJobs;

namespace AiPrReview.Functions;  // Keep namespace consistent

public class ReviewPullRequestQueueFunction
{
    private readonly ReviewOrchestrator _orchestrator;
    private readonly ILogger<ReviewPullRequestQueueFunction> _logger;

    public ReviewPullRequestQueueFunction(
        ReviewOrchestrator orchestrator,
        ILogger<ReviewPullRequestQueueFunction> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    [Function("ReviewPullRequestQueue")]
    public async Task Run([QueueTrigger("pr-reviews")] PrReviewQueueMessage message)
    {
        _logger.LogInformation("Processing queued review for repository {RepoId}, PR {PrId}",
            message.RepositoryId, message.PullRequestId);
        try
        {
            await _orchestrator.ExecuteReviewAsync(message.RepositoryId, message.PullRequestId, message.Provider);
            _logger.LogInformation("Successfully processed queued review for repository {RepoId}, PR {PrId}",
                message.RepositoryId, message.PullRequestId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing queued PR review for repository {RepoId}, PR {PrId}, {Message}",
                message.RepositoryId, message.PullRequestId, ex.Message);
        }
    }
}
