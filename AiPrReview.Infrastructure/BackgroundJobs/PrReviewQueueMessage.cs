namespace AiPrReview.Infrastructure.BackgroundJobs;

public class PrReviewQueueMessage
{
    public string RepositoryId { get; set; } = string.Empty;
    public int PullRequestId { get; set; }
    public DateTime EnqueuedAt { get; set; }
}