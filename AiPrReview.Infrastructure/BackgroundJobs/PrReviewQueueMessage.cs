namespace AiPrReview.Infrastructure.BackgroundJobs;

public class PrReviewQueueMessage
{
    public string RepositoryId { get; set; } = string.Empty;
    public int PullRequestId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public DateTime EnqueuedAt { get; set; }
}