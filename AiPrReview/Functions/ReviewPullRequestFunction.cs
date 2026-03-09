using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using AiPrReview.Application.Interfaces;
using AiPrReview.Application.Services;
using AiPrReview.Infrastructure.BackgroundJobs;

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

        if (input == null || string.IsNullOrEmpty(input.RepositoryId) || input.PullRequestId <= 0)
        {
            httpResponse.StatusCode = HttpStatusCode.BadRequest;
            await httpResponse.WriteStringAsync("Invalid request. Expected { repositoryId, pullRequestId }.");
            return new ReviewResponse { HttpResponse = httpResponse };
        }

        try
        {
            int fileCount = await EstimateFileCountAsync(input.RepositoryId, input.PullRequestId);
            const int threshold = 25;

            if (fileCount < threshold)
            {
                // Process Immediately
                await _orchestrator.ExecuteReviewAsync(input.RepositoryId, input.PullRequestId);
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
                    RepositoryId = input.RepositoryId,
                    PullRequestId = input.PullRequestId,
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
            _logger.LogError(ex, "Error processing PR review for repository {RepoId}, PR {PrId}", input.RepositoryId, input.PullRequestId);
            httpResponse.StatusCode = HttpStatusCode.InternalServerError;
            await httpResponse.WriteStringAsync("An error occurred while processing the PR review.");
            return new ReviewResponse { HttpResponse = httpResponse };
        }
    }

    private async Task<int> EstimateFileCountAsync(string repositoryId, int pullRequestId)
    {
        try
        {
            var repoConfig = await _configService.GetRepositoryConfigByNameAsync(repositoryId);
            if (repoConfig == null || !repoConfig.IsActive) return 0;

            var repoProvider = _repoProviderFactory.Create(repoConfig);
            var files = await repoProvider.GetChangedFilesAsync(repoConfig.ApiUrl, pullRequestId);
            return files.Count;
        }
        catch { return 0; }
    }
}

public record ReviewRequest(string RepositoryId, int PullRequestId);