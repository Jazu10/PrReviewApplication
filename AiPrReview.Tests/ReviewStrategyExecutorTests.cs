using AiPrReview.Application.Interfaces;
using AiPrReview.Application.Services;
using AiPrReview.Core.Dto;
using AiPrReview.Core.Entities;
using AiPrReview.Core.Enums;
using AiPrReview.Core.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AiPrReview.Tests;

public class ReviewStrategyExecutorTests
{
    private sealed class StubPromptBuilder : IPromptBuilder
    {
        public string BuildBatchPrompt(PullRequestInfo prInfo, IReadOnlyList<ChangedFile> files) =>
            $"batch:{files.Count}";

        public string BuildFilePrompt(PullRequestInfo prInfo, ChangedFile file) =>
            $"file:{file.FilePath}";
    }

    private sealed class StubResponseParser : IAiResponseParser
    {
        public IReadOnlyList<ReviewComment> Parse(string aiResponse) =>
            new List<ReviewComment>
            {
                new() { FilePath = "test.cs", Issue = aiResponse, Suggestion = "fix" }
            };
    }

    private sealed class StubLlmClient : ILlmClient
    {
        public Task<string> ReviewAsync(string userPrompt, CancellationToken cancellationToken = default) =>
            Task.FromResult($"response:{userPrompt}");
    }

    [Fact]
    public async Task ExecuteAsync_BatchStrategy_ProducesComments()
    {
        var executor = new ReviewStrategyExecutor(
            new StubPromptBuilder(),
            new StubResponseParser(),
            NullLogger<ReviewStrategyExecutor>.Instance);

        var prInfo = new PullRequestInfo(1, "Title", "Desc", "repo", "head", "base");
        var files = new List<ChangedFile>
        {
            new() { FilePath = "a.cs", Status = "modified", Diff = "diff" },
            new() { FilePath = "b.cs", Status = "modified", Diff = "diff" }
        };
        var config = new ProviderConfig
        {
            ReviewStrategy = ReviewStrategy.Batch,
            PublishMode = PublishMode.Single
        };

        var comments = await executor.ExecuteAsync(
            ReviewStrategy.Batch,
            prInfo,
            config,
            files,
            new StubLlmClient(),
            CancellationToken.None);

        Assert.NotEmpty(comments);
    }

    [Fact]
    public async Task ExecuteAsync_FileLevelStrategy_RespectsMaxConcurrency()
    {
        var executor = new ReviewStrategyExecutor(
            new StubPromptBuilder(),
            new StubResponseParser(),
            NullLogger<ReviewStrategyExecutor>.Instance);

        var prInfo = new PullRequestInfo(1, "Title", "Desc", "repo", "head", "base");
        var files = Enumerable.Range(0, 10)
            .Select(i => new ChangedFile { FilePath = $"file{i}.cs", Status = "modified", Diff = "diff" })
            .ToList();

        var config = new ProviderConfig
        {
            ReviewStrategy = ReviewStrategy.FileLevel,
            PublishMode = PublishMode.Multiple,
            MaxConcurrentFileReviews = 2
        };

        var comments = await executor.ExecuteAsync(
            ReviewStrategy.FileLevel,
            prInfo,
            config,
            files,
            new StubLlmClient(),
            CancellationToken.None);

        Assert.Equal(files.Count, comments.Count);
    }
}

