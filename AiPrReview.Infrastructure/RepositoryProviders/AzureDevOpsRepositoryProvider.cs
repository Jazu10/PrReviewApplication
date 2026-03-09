using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using AiPrReview.Core.Interfaces;
using AiPrReview.Core.Dto;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using AiPrReview.Core.Enums;

namespace AiPrReview.Infrastructure.RepositoryProviders;

public class AzureDevOpsRepositoryProvider : IRepositoryProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _apiUrl;
    private readonly string _organization;
    private readonly string _project;
    private readonly string _repositoryId;
    private readonly string _baseUrl;
    private readonly ILogger<AzureDevOpsRepositoryProvider> _logger;

    public AzureDevOpsRepositoryProvider(
        string apiUrl,
        string organization,
        string project,
        string repositoryId,
        string accessToken,
        ILogger<AzureDevOpsRepositoryProvider> logger)
    {
        _apiUrl = apiUrl;
        _organization = organization;
        _project = project;
        _repositoryId = repositoryId;
        _logger = logger;

        _baseUrl = $"{_apiUrl.TrimEnd('/')}/{_organization}/{Uri.EscapeDataString(_project)}/_apis/git/repositories/{Uri.EscapeDataString(_repositoryId)}";

        _httpClient = new HttpClient();
        var patBytes = Encoding.ASCII.GetBytes($":{accessToken}");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(patBytes));

        _logger.LogInformation("AzureDevOpsRepositoryProvider initialized for Org: {Org}, Project: {Project}, Repo: {Repo}", _organization, _project, _repositoryId);
    }

    public async Task<PullRequestInfo> GetPullRequestInfoAsync(string repoApiUrl, int prId)
    {
        _logger.LogInformation("Fetching info for PR #{PrId}", prId);
        var url = $"{_baseUrl}/pullrequests/{prId}?api-version=7.0";

        try
        {
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var info = new PullRequestInfo(
                Id: root.GetProperty("pullRequestId").GetInt32(),
                Title: root.GetProperty("title").GetString() ?? "",
                Description: root.GetProperty("description").GetString() ?? "",
                RepositoryName: $"{_project}/{_repositoryId}",
                HeadSha: root.GetProperty("lastMergeSourceCommit").GetProperty("commitId").GetString() ?? "",
                BaseSha: root.GetProperty("lastMergeTargetCommit").GetProperty("commitId").GetString() ?? ""
            );

            _logger.LogDebug("Successfully retrieved info for PR #{PrId}. HeadSha: {Head}, BaseSha: {Base}", prId, info.HeadSha, info.BaseSha);
            return info;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve info for PR #{PrId}", prId);
            throw;
        }
    }

    public async Task<IReadOnlyList<ChangedFile>> GetChangedFilesAsync(string repoApiUrl, int prId)
    {
        _logger.LogInformation("Fetching changed files list (metadata only) for PR #{PrId}", prId);

        try
        {
            var prInfo = await GetPullRequestInfoAsync(repoApiUrl, prId);
            var url = $"{_baseUrl}/diffs/commits?baseVersion={prInfo.BaseSha}&baseVersionType=commit&targetVersion={prInfo.HeadSha}&targetVersionType=commit&api-version=7.0";

            var json = await _httpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("changes", out var changes))
            {
                _logger.LogWarning("No changes found in PR #{PrId}", prId);
                return new List<ChangedFile>();
            }

            var changedFiles = changes.EnumerateArray()
                .Where(c => c.TryGetProperty("item", out var item) && item.GetProperty("gitObjectType").GetString() == "blob")
                .Select(c => new ChangedFile
                {
                    FilePath = c.GetProperty("item").GetProperty("path").GetString()!,
                    Status = c.GetProperty("changeType").GetString()!
                }).ToList();

            _logger.LogInformation("Found {Count} changed files for PR #{PrId}", changedFiles.Count, prId);
            return changedFiles;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching changed files metadata for PR #{PrId}", prId);
            throw;
        }
    }

    public async Task<IReadOnlyList<ChangedFile>> GetPullRequestChangesAsync(string repoApiUrl, int prId)
    {
        _logger.LogInformation("Fetching full changes (content & diffs) for PR #{PrId}", prId);

        try
        {
            var prInfo = await GetPullRequestInfoAsync(repoApiUrl, prId);
            var diffUrl = $"{_baseUrl}/diffs/commits?baseVersion={prInfo.BaseSha}&baseVersionType=commit&targetVersion={prInfo.HeadSha}&targetVersionType=commit&api-version=7.0";

            var diffResponse = await _httpClient.GetAsync(diffUrl);
            if (!diffResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Diff API returned {StatusCode} for PR #{PrId}", diffResponse.StatusCode, prId);
                return new List<ChangedFile>();
            }

            var diffJson = await diffResponse.Content.ReadAsStringAsync();
            using var diffDoc = JsonDocument.Parse(diffJson);
            if (!diffDoc.RootElement.TryGetProperty("changes", out var changes)) return new List<ChangedFile>();

            var results = new ConcurrentBag<ChangedFile>();
            var changeArray = changes.EnumerateArray().ToArray();

            _logger.LogInformation("Processing {Count} changed items in parallel for PR #{PrId}", changeArray.Length, prId);

            await Parallel.ForEachAsync(changeArray, new ParallelOptions { MaxDegreeOfParallelism = 5 }, async (change, ct) =>
            {
                if (!change.TryGetProperty("item", out var item) || item.GetProperty("gitObjectType").GetString() != "blob") return;

                var path = item.GetProperty("path").GetString()!;
                var changeType = (change.GetProperty("changeType").GetString() ?? "edit").ToLower();

                _logger.LogDebug("Fetching content for {Path} [{ChangeType}]", path, changeType);

                string oldContent = !changeType.Contains("add") ? await GetFileContentAsync(repoApiUrl, path, prInfo.BaseSha) : "";
                string newContent = !changeType.Contains("delete") ? await GetFileContentAsync(repoApiUrl, path, prInfo.HeadSha) : "";

                results.Add(new ChangedFile
                {
                    FilePath = path,
                    Status = changeType,
                    Diff = GenerateUnifiedDiff(path, oldContent, newContent),
                    Content = newContent
                });
            });

            _logger.LogInformation("Successfully processed full changes for PR #{PrId}", prId);
            return results.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching full changes for PR #{PrId}", prId);
            throw;
        }
    }

    public async Task<string> GetFileDiffAsync(string repoApiUrl, int prId, string filePath)
    {
        _logger.LogInformation("Generating isolated diff for file {FilePath} in PR #{PrId}", filePath, prId);
        var prInfo = await GetPullRequestInfoAsync(repoApiUrl, prId);
        var oldContent = await GetFileContentAsync(repoApiUrl, filePath, prInfo.BaseSha);
        var newContent = await GetFileContentAsync(repoApiUrl, filePath, prInfo.HeadSha);

        return GenerateUnifiedDiff(filePath, oldContent, newContent);
    }

    public async Task<string> GetFileContentAsync(string repoApiUrl, string filePath, string commitSha)
    {
        if (string.IsNullOrEmpty(commitSha)) return string.Empty;

        var normalizedPath = filePath.StartsWith("/") ? filePath.Substring(1) : filePath;

        var url = $"{_baseUrl}/items?path={Uri.EscapeDataString(normalizedPath)}" +
                  $"&versionDescriptor.version={commitSha}" +
                  $"&versionDescriptor.versionType=commit" +
                  $"&includeContent=true&api-version=7.0";

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Could not fetch content for {FilePath} at commit {CommitSha}. StatusCode: {StatusCode}", normalizedPath, commitSha, response.StatusCode);
                return string.Empty;
            }

            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while fetching content for {FilePath} at commit {CommitSha}", normalizedPath, commitSha);
            return string.Empty;
        }
    }

    public async Task PublishReviewCommentsAsync(string repoApiUrl, int prId, IReadOnlyList<ReviewComment> comments, PublishMode publishMode)
    {
        if (comments == null || !comments.Any()) return;

        var threadUrl = $"{_baseUrl}/pullrequests/{prId}/threads?api-version=7.0";

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
                sb.AppendLine("---"); // Separator between files in the single comment
            }

            await PostToAzureDevOps(threadUrl, sb.ToString(), null);
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

                // Passing the filePath in threadContext pins the comment to that specific file in the UI
                await PostToAzureDevOps(threadUrl, sb.ToString(), comment.FilePath);
            }
        }
    }

    private async Task PostToAzureDevOps(string url, string content, string? filePath)
    {
        var payload = new
        {
            comments = new[] { new { content = content, commentType = 1 } },
            status = 1, // Active
            threadContext = filePath != null ? new { filePath = filePath } : null
        };

        var json = JsonSerializer.Serialize(payload);
        var body = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(url, body);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Failed to post comment. Status: {Status}, Error: {Error}", response.StatusCode, error);
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
}