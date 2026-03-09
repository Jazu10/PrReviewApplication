using Octokit;
using AiPrReview.Core.Interfaces;
using Microsoft.Extensions.Logging;
using AiPrReview.Core.Dto;
using AiPrReview.Core.Enums;

namespace AiPrReview.Infrastructure.RepositoryProviders;

public class GitHubRepositoryProvider : IRepositoryProvider
{
    private readonly string _apiUrl;
    private readonly GitHubClient _client;
    private readonly string _repoOwner;
    private readonly string _repoName;
    private readonly ILogger<GitHubRepositoryProvider> _logger;

    public GitHubRepositoryProvider(
        string apiUrl,
        string token,
        string owner,
        string repoName,
        ILogger<GitHubRepositoryProvider> logger)
    {
        _apiUrl = apiUrl;
        _client = new GitHubClient(new ProductHeaderValue("AiPrReview"), new Uri(_apiUrl));
        _client.Credentials = new Credentials(token);
        _repoOwner = owner;
        _repoName = repoName;
        _logger = logger;
    }

    public async Task<PullRequestInfo> GetPullRequestInfoAsync(string repoApiUrl, int prId)
    {
        try
        {
            var pr = await _client.PullRequest.Get(_repoOwner, _repoName, prId);
            return new PullRequestInfo(
                pr.Number,
                pr.Title ?? "",
                pr.Body ?? "",
                $"{_repoOwner}/{_repoName}",
                pr.Head.Sha,
                pr.Base.Sha
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get PR info for PR {PrId} in {Owner}/{Repo}", prId, _repoOwner, _repoName);
            throw;
        }
    }

    public async Task<IReadOnlyList<ChangedFile>> GetPullRequestChangesAsync(string repoApiUrl, int prId)
    {
        try
        {
            var files = await _client.PullRequest.Files(_repoOwner, _repoName, prId);
            var changedFiles = new List<ChangedFile>();

            foreach (var file in files)
            {
                var status = file.Status switch
                {
                    "added" => "added",
                    "removed" => "deleted",
                    "renamed" => "renamed",
                    _ => "modified"
                };

                changedFiles.Add(new ChangedFile
                {
                    FilePath = file.FileName,
                    Status = status,
                    Diff = file.Patch ?? "" // GitHub provides patch in unified diff format
                });
            }

            return changedFiles;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch changed files for PR {PrId} in {Owner}/{Repo}", prId, _repoOwner, _repoName);
            throw;
        }
    }

    public async Task<IReadOnlyList<ChangedFile>> GetChangedFilesAsync(string repoApiUrl, int prId)
    {
        // This method is now just a wrapper; but we implement it directly if needed.
        // However, the interface already has GetPullRequestChangesAsync, so we could
        // keep this for backward compatibility or simply call GetPullRequestChangesAsync.
        return await GetPullRequestChangesAsync(repoApiUrl, prId);
    }

    public async Task<string> GetFileDiffAsync(string repoApiUrl, int prId, string filePath)
    {
        try
        {
            // Use the already fetched changes if possible, but for simplicity,
            // we call the API again (or could cache). This method might be deprecated.
            var files = await _client.PullRequest.Files(_repoOwner, _repoName, prId);
            var file = files.FirstOrDefault(f => f.FileName.Equals(filePath, StringComparison.OrdinalIgnoreCase));
            return file?.Patch ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get diff for file {FilePath} in PR {PrId}", filePath, prId);
            return string.Empty;
        }
    }

    public async Task<string> GetFileContentAsync(string repoApiUrl, string filePath, string commitSha)
    {
        try
        {
            var contents = await _client.Repository.Content.GetAllContentsByRef(
                _repoOwner,
                _repoName,
                filePath,
                commitSha);

            return contents.FirstOrDefault()?.Content ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get content for file {FilePath} at commit {CommitSha}", filePath, commitSha);
            return string.Empty;
        }
    }

    public async Task PublishReviewCommentsAsync(string repoApiUrl, int prId, IReadOnlyList<ReviewComment> comments, PublishMode publishMode)
    {
        try
        {
            var reviewComments = comments.Select(c => new DraftPullRequestReviewComment(
                $"{c.Issue}\n\n**Suggestion:**\n{c.Suggestion}",
                c.FilePath,
                c.Line ?? 1
            )).ToList();

            var review = new PullRequestReviewCreate
            {
                Event = PullRequestReviewEvent.Comment,
                Comments = reviewComments
            };

            await _client.PullRequest.Review.Create(_repoOwner, _repoName, prId, review);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish comments to PR {PrId}", prId);
            throw;
        }
    }
}