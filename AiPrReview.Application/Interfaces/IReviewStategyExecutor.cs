using AiPrReview.Core.Dto;
using AiPrReview.Core.Entities;
using AiPrReview.Core.Enums;
using AiPrReview.Core.Interfaces;

namespace AiPrReview.Application.Interfaces
{
    public interface IReviewStrategyExecutor
    {
        Task<IReadOnlyList<ReviewComment>> ExecuteAsync(
            ReviewStrategy strategy,
            PullRequestInfo prInfo,
            ProviderConfig config,
            IReadOnlyList<ChangedFile> files,
            ILlmClient llmClient,
            CancellationToken cancellationToken);
    }
}
