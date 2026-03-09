using AiPrReview.Core.Dto;

namespace AiPrReview.Core.Interfaces;

public interface IRepositoryProvider
{
    Task<PullRequestInfo> GetPullRequestInfoAsync(string repoApiUrl, int prId);

    Task<IReadOnlyList<ChangedFile>> GetChangedFilesAsync(string repoApiUrl, int prId);

    Task<IReadOnlyList<ChangedFile>> GetPullRequestChangesAsync(string repoApiUrl, int prId);

    Task<string> GetFileDiffAsync(string repoApiUrl, int prId, string filePath);

    Task<string> GetFileContentAsync(string repoApiUrl, string filePath, string commitSha);

    Task PublishReviewCommentsAsync(string repoApiUrl, int prId, IReadOnlyList<ReviewComment> comments);
}