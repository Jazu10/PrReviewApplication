using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using AiPrReview.Application.Interfaces;
using AiPrReview.Application.Services;
using AiPrReview.Infrastructure.BackgroundJobs;
using AiPrReview.Core.Enums;

namespace AiPrReview.Functions;

// 1. Define a Multi-Output class to resolve the [QueueOutput] parameter error
public class ReviewResponse
{
    [HttpResult]
    public HttpResponseData HttpResponse { get; set; } = null!;

    // Ensure the queue name matches your QueueTrigger listener ("pr-reviews")
    [QueueOutput("pr-reviews", Connection = "AzureWebJobsStorage")]
    public PrReviewQueueMessage? Message { get; set; }
}

public class ReviewPullRequestFunction
{
    private readonly IConfigurationService _configService;
    private readonly IRepositoryProviderFactory _repoProviderFactory;
    private readonly ReviewOrchestrator _orchestrator;
    private readonly ILogger<ReviewPullRequestFunction> _logger;

    public ReviewPullRequestFunction(
        IConfigurationService configService,
        IRepositoryProviderFactory repoProviderFactory,
        ReviewOrchestrator orchestrator,
        ILogger<ReviewPullRequestFunction> logger)
    {
        _configService = configService;
        _repoProviderFactory = repoProviderFactory;
        _orchestrator = orchestrator;
        _logger = logger;
    }

    [Function("ReviewPullRequest")]
    public async Task<ReviewResponse> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req,
        FunctionContext executionContext)
    {
        var httpResponse = req.CreateResponse();
        PrReviewQueueMessage? queueMessage = null;

        // Parse request body
        ReviewRequest? input;
        try
        {
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            input = JsonSerializer.Deserialize<ReviewRequest>(requestBody, options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse request body");
            httpResponse.StatusCode = HttpStatusCode.BadRequest;
            await httpResponse.WriteStringAsync("Invalid JSON request body.");
            return new ReviewResponse { HttpResponse = httpResponse };
        }

        if (input == null || string.IsNullOrEmpty(input.RepoName) || input.PullRequestId <= 0)
        {
            httpResponse.StatusCode = HttpStatusCode.BadRequest;
            await httpResponse.WriteStringAsync("Invalid request. Expected { repoName, pullRequestId, provider }.");
            return new ReviewResponse { HttpResponse = httpResponse };
        }

        try
        {
            int fileCount = await EstimateFileCountAsync(input.RepoName, input.PullRequestId, input.Provider);
            const int threshold = 25;

            if (fileCount < threshold)
            {
                // Process Immediately
                await _orchestrator.ExecuteReviewAsync(input.RepoName, input.PullRequestId, input.Provider);
                httpResponse.StatusCode = HttpStatusCode.OK;
                await httpResponse.WriteAsJsonAsync(new
                {
                    message = "Review completed successfully.",
                    status = "completed"
                });
            }
            else
            {
                // Queue for Background Processing
                queueMessage = new PrReviewQueueMessage
                {
                    RepositoryId = input.RepoName,
                    PullRequestId = input.PullRequestId,
                    Provider = input.Provider,
                    EnqueuedAt = DateTime.UtcNow
                };

                httpResponse.StatusCode = HttpStatusCode.Accepted;
                httpResponse.Headers.Add("Location", $"https://{req.Url.Host}/api/review-status/{Guid.NewGuid()}");
                await httpResponse.WriteAsJsonAsync(new
                {
                    message = "Review queued due to size.",
                    status = "queued"
                });
            }

            return new ReviewResponse
            {
                HttpResponse = httpResponse,
                Message = queueMessage
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing PR review for repository {RepoId}, PR {PrId}", input.RepoName, input.PullRequestId);
            httpResponse.StatusCode = HttpStatusCode.InternalServerError;
            await httpResponse.WriteStringAsync("An error occurred while processing the PR review.");
            return new ReviewResponse { HttpResponse = httpResponse };
        }
    }

    private async Task<int> EstimateFileCountAsync(string repoName, int pullRequestId, string provider)
    {
        try
        {
            // Convert the provider string to the ProviderType enum
            if (!Enum.TryParse<ProviderType>(provider, true, out var providerEnum))
            {
                throw new ArgumentException($"Invalid provider type: {provider}");
            }

            // Get global provider config
            var providerConfig = await _configService.GetProviderConfigAsync(providerEnum);

            // Create repository provider using config and repo name
            var repoProvider = _repoProviderFactory.Create(providerConfig, repoName);

            // Fetch changed files (metadata only, no diffs)
            var files = await repoProvider.GetChangedFilesAsync("", pullRequestId);
            return files.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to estimate file count for {RepoName}, PR {PrId}. Falling back to small.", repoName, pullRequestId);
            return 0;
        }
    }
}

public record ReviewRequest(string RepoName, int PullRequestId, string Provider);