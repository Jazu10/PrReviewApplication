using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Octokit;
using AiPrReview.Core.Interfaces;
using Microsoft.Extensions.Logging;
using AiPrReview.Core.Dto;
using AiPrReview.Core.Enums;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using ChangeType = DiffPlex.DiffBuilder.Model.ChangeType;

namespace AiPrReview.Infrastructure.RepositoryProviders
{
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

            var uri = new Uri(string.IsNullOrWhiteSpace(_apiUrl) ? "https://api.github.com/" : _apiUrl);

            _client = new GitHubClient(new ProductHeaderValue("AiPrReview"), uri)
            {
                Credentials = new Credentials(token)
            };

            _repoOwner = owner;
            _repoName = repoName;
            _logger = logger;

            _logger.LogInformation("GitHubRepositoryProvider initialized for Owner: {Owner}, Repo: {Repo}", _repoOwner, _repoName);
        }

        public async Task<PullRequestInfo> GetPullRequestInfoAsync(string repoApiUrl, int prId)
        {
            _logger.LogInformation("Fetching info for PR #{PrId}", prId);
            try
            {
                var pr = await _client.PullRequest.Get(_repoOwner, _repoName, prId);
                var info = new PullRequestInfo(
                    Id: pr.Number,
                    Title: pr.Title ?? "",
                    Description: pr.Body ?? "",
                    RepositoryName: $"{_repoOwner}/{_repoName}",
                    HeadSha: pr.Head.Sha,
                    BaseSha: pr.Base.Sha
                );

                _logger.LogDebug("Successfully retrieved info for PR #{PrId}. HeadSha: {Head}, BaseSha: {Base}", prId, info.HeadSha, info.BaseSha);
                return info;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get PR info for PR {PrId} in {Owner}/{Repo}", prId, _repoOwner, _repoName);
                throw new InvalidOperationException($"Failed to retrieve PR info for PR #{prId}", ex);
            }
        }

        public async Task<IReadOnlyList<ChangedFile>> GetChangedFilesAsync(string repoApiUrl, int prId)
        {
            _logger.LogInformation("Fetching changed files list (metadata only) for PR #{PrId}", prId);
            try
            {
                var files = await _client.PullRequest.Files(_repoOwner, _repoName, prId);

                var changedFiles = files
                    .Where(f => IsReviewableFile(f.FileName))
                    .Select(f => new ChangedFile
                    {
                        FilePath = f.FileName,
                        Status = MapStatus(f.Status)
                    }).ToList();

                _logger.LogInformation("Found {Count} reviewable changed files for PR #{PrId}", changedFiles.Count, prId);
                return changedFiles;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching changed files metadata for PR #{PrId}", prId);
                throw new InvalidOperationException($"Error fetching changed files metadata for PR #{prId}", ex);
            }
        }

        public async Task<IReadOnlyList<ChangedFile>> GetPullRequestChangesAsync(string repoApiUrl, int prId)
        {
            _logger.LogInformation("Fetching full changes (content & diffs) for PR #{PrId}", prId);
            try
            {
                var prInfo = await GetPullRequestInfoAsync(repoApiUrl, prId);
                var files = await _client.PullRequest.Files(_repoOwner, _repoName, prId);

                var results = new ConcurrentBag<ChangedFile>();

                // Pre-filter to avoid hitting OpenAI limits with binary/lock files
                var reviewableFiles = files.Where(f => IsReviewableFile(f.FileName)).ToList();

                _logger.LogInformation("Processing {Count} reviewable changed items in parallel for PR #{PrId}", reviewableFiles.Count, prId);

                await Parallel.ForEachAsync(reviewableFiles, new ParallelOptions { MaxDegreeOfParallelism = 5 }, async (file, ct) =>
                {
                    var status = MapStatus(file.Status);

                    // Handle file renames properly to prevent missing old-content fetches
                    string oldPath = status == "rename" && !string.IsNullOrEmpty(file.PreviousFileName)
                        ? file.PreviousFileName
                        : file.FileName;

                    _logger.LogDebug("Fetching content for {Path} [{ChangeType}]", file.FileName, status);

                    string oldContent = !status.Contains("add") ? await GetFileContentAsync(repoApiUrl, oldPath, prInfo.BaseSha) : "";
                    string newContent = !status.Contains("delete") ? await GetFileContentAsync(repoApiUrl, file.FileName, prInfo.HeadSha) : "";

                    results.Add(new ChangedFile
                    {
                        FilePath = file.FileName,
                        Status = status,
                        // Prefer GitHub's safe native patch. Fallback to DiffPlex ONLY if missing.
                        Diff = GenerateUnifiedDiff(file.FileName, oldContent, newContent),
                        Content = newContent
                    });
                });

                var finalResults = results.ToList();
                _logger.LogInformation("Successfully processed full changes for PR #{PrId}", prId);
                return finalResults;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch full changes for PR {PrId} in {Owner}/{Repo}", prId, _repoOwner, _repoName);
                throw new InvalidOperationException($"Error fetching full changes for PR #{prId}", ex);
            }
        }

        public async Task<string> GetFileDiffAsync(string repoApiUrl, int prId, string filePath)
        {
            _logger.LogInformation("Generating isolated diff for file {FilePath} in PR #{PrId}", filePath, prId);

            // Prefer fetching patch directly if available without downloading full blobs
            var files = await _client.PullRequest.Files(_repoOwner, _repoName, prId);
            var targetFile = files.FirstOrDefault(f => f.FileName.Equals(filePath, StringComparison.OrdinalIgnoreCase));

            if (targetFile != null && !string.IsNullOrWhiteSpace(targetFile.Patch))
            {
                return targetFile.Patch;
            }

            var prInfo = await GetPullRequestInfoAsync(repoApiUrl, prId);
            var oldContent = await GetFileContentAsync(repoApiUrl, filePath, prInfo.BaseSha);
            var newContent = await GetFileContentAsync(repoApiUrl, filePath, prInfo.HeadSha);

            return GenerateUnifiedDiff(filePath, oldContent, newContent);
        }

        public async Task<string> GetFileContentAsync(string repoApiUrl, string filePath, string commitSha)
        {
            if (string.IsNullOrEmpty(commitSha)) return string.Empty;

            try
            {
                var contents = await _client.Repository.Content.GetAllContentsByRef(
                    _repoOwner,
                    _repoName,
                    filePath,
                    commitSha);

                return contents.FirstOrDefault()?.Content ?? string.Empty;
            }
            catch (NotFoundException wrn)
            {
                _logger.LogDebug(wrn, "Content not found for {FilePath} at commit {CommitSha}", filePath, commitSha);
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception while fetching content for {FilePath} at commit {CommitSha}", filePath, commitSha);
                return string.Empty;
            }
        }

        public async Task PublishReviewCommentsAsync(string repoApiUrl, int prId, IReadOnlyList<ReviewComment> comments, PublishMode publishMode)
        {
            if (comments == null || !comments.Any()) return;

            try
            {
                if (publishMode == PublishMode.Single)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("## 🤖 AI Pull Request Review Summary");
                    sb.AppendLine("---");

                    foreach (var comment in comments)
                    {
                        sb.AppendLine($"### 📄 `{Path.GetFileName(comment.FilePath)}` ");
                        sb.AppendLine($"**🔍 Analysis:** {comment.Issue.Trim()}");
                        sb.AppendLine();
                        sb.AppendLine("**💡 Suggested Fix:**");
                        sb.AppendLine("```csharp");
                        sb.AppendLine(comment.Suggestion.Trim());
                        sb.AppendLine("```");
                        sb.AppendLine("---");
                    }

                    var review = new PullRequestReviewCreate
                    {
                        Event = PullRequestReviewEvent.Comment,
                        Body = sb.ToString()
                    };

                    await _client.PullRequest.Review.Create(_repoOwner, _repoName, prId, review);
                }
                else
                {
                    foreach (var comment in comments)
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine($"### 🤖 AI Review: `{Path.GetFileName(comment.FilePath)}` ");
                        sb.AppendLine("---");
                        sb.AppendLine($"**🔍 Analysis:** {comment.Issue.Trim()}");
                        sb.AppendLine();
                        sb.AppendLine("**💡 Suggested Fix:**");
                        sb.AppendLine("```csharp");
                        sb.AppendLine(comment.Suggestion.Trim());
                        sb.AppendLine("```");

                        await _client.Issue.Comment.Create(_repoOwner, _repoName, prId, sb.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish comments to PR {PrId}", prId);
                throw;
            }
        }

        private string GenerateUnifiedDiff(string filePath, string oldContent, string newContent)
        {
            if (string.IsNullOrEmpty(oldContent) && string.IsNullOrEmpty(newContent)) return "";

            try
            {
                var diffBuilder = new InlineDiffBuilder(new Differ());
                var diffModel = diffBuilder.BuildDiffModel(oldContent ?? "", newContent ?? "");

                var sb = new StringBuilder();
                sb.AppendLine($"--- a/{filePath}");
                sb.AppendLine($"+++ b/{filePath}");
                foreach (var line in diffModel.Lines)
                {
                    switch (line.Type)
                    {
                        case ChangeType.Inserted: sb.AppendLine($"+{line.Text}"); break;
                        case ChangeType.Deleted: sb.AppendLine($"-{line.Text}"); break;
                        case ChangeType.Unchanged: sb.AppendLine($" {line.Text}"); break;
                    }
                }
                return sb.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate diff for {FilePath}", filePath);
                return "Diff generation failed.";
            }
        }

        private static string MapStatus(string octokitStatus)
        {
            return (octokitStatus?.ToLower()) switch
            {
                "added" => "add",
                "removed" => "delete",
                "renamed" => "rename",
                _ => "edit"
            };
        }

        private static bool IsReviewableFile(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return false;

            var lowerName = fileName.ToLowerInvariant();

            // Skip massive auto-generated files that eat up tokens
            if (lowerName.EndsWith("package-lock.json") ||
                lowerName.EndsWith("yarn.lock") ||
                lowerName.EndsWith("pnpm-lock.yaml"))
            {
                return false;
            }

            // Skip binary files
            var ext = Path.GetExtension(lowerName);
            var binaryExtensions = new HashSet<string>
            {
                ".png", ".jpg", ".jpeg", ".gif", ".ico", ".svg", ".webp",
                ".dll", ".exe", ".pdb", ".bin", ".so", ".dylib",
                ".zip", ".tar", ".gz", ".7z", ".rar",
                ".pdf", ".doc", ".docx", ".xls", ".xlsx",
                ".ttf", ".woff", ".woff2", ".eot", ".mp4", ".mp3"
            };

            return !binaryExtensions.Contains(ext);
        }
    }
}