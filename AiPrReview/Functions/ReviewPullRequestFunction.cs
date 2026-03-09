using System.Net;
using System.Text.Json;
using AiPrReview.Application.Exceptions;
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

        try
        {
            var input = await ParseAndValidateRequestAsync(req, httpResponse);
            if (input is null)
            {
                // Response has already been written for bad request
                return new ReviewResponse { HttpResponse = httpResponse };
            }

            var decision = await DecideProcessingModeAsync(input, executionContext.CancellationToken);

            if (decision.ProcessInline)
            {
                await ProcessInlineAsync(input, httpResponse, executionContext.CancellationToken);

                return new ReviewResponse
                {
                    HttpResponse = httpResponse
                };
            }

            var queueMessage = CreateQueueMessage(input);
            await WriteQueuedResponseAsync(httpResponse, req);

            return new ReviewResponse
            {
                HttpResponse = httpResponse,
                Message = queueMessage
            };
        }
        catch (ProviderNotFoundException ex)
        {
            _logger.LogWarning(ex, "Provider not found while processing PR review request.");
            httpResponse.StatusCode = HttpStatusCode.BadRequest;
            await httpResponse.WriteStringAsync("The specified provider is not configured or inactive.");
            return new ReviewResponse { HttpResponse = httpResponse };
        }
        catch (NoActiveLlmProviderException ex)
        {
            _logger.LogError(ex, "No active LLM provider available to process PR review.");
            httpResponse.StatusCode = HttpStatusCode.ServiceUnavailable;
            await httpResponse.WriteStringAsync("No active AI provider is configured. Please try again later.");
            return new ReviewResponse { HttpResponse = httpResponse };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing PR review HTTP request.");
            httpResponse.StatusCode = HttpStatusCode.InternalServerError;
            await httpResponse.WriteStringAsync("An unexpected error occurred while processing the PR review.");
            return new ReviewResponse { HttpResponse = httpResponse };
        }
    }

    private static async Task<ReviewRequest?> ParseAndValidateRequestAsync(HttpRequestData req, HttpResponseData httpResponse)
    {
        ReviewRequest? input;
        try
        {
            using var reader = new StreamReader(req.Body);
            var requestBody = await reader.ReadToEndAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            input = JsonSerializer.Deserialize<ReviewRequest>(requestBody, options);
        }
        catch (Exception)
        {
            httpResponse.StatusCode = HttpStatusCode.BadRequest;
            await httpResponse.WriteStringAsync("Invalid JSON request body.");
            return null;
        }

        if (input is null || string.IsNullOrWhiteSpace(input.RepoName) || input.PullRequestId <= 0 || string.IsNullOrWhiteSpace(input.Provider))
        {
            httpResponse.StatusCode = HttpStatusCode.BadRequest;
            await httpResponse.WriteStringAsync("Invalid request. Expected { repoName, pullRequestId, provider }.");
            return null;
        }

        return input;
    }

    private async Task<(bool ProcessInline, int FileCount)> DecideProcessingModeAsync(ReviewRequest input, CancellationToken cancellationToken)
    {
        const int threshold = 25;
        var fileCount = await EstimateFileCountAsync(input.RepoName, input.PullRequestId, input.Provider, cancellationToken);

        return (fileCount < threshold, fileCount);
    }

    private async Task ProcessInlineAsync(ReviewRequest input, HttpResponseData httpResponse, CancellationToken cancellationToken)
    {
        await _orchestrator.ExecuteReviewAsync(input.RepoName, input.PullRequestId, input.Provider, cancellationToken);

        httpResponse.StatusCode = HttpStatusCode.OK;
        await httpResponse.WriteAsJsonAsync(new
        {
            message = "Review completed successfully.",
            status = "completed"
        });
    }

    private static PrReviewQueueMessage CreateQueueMessage(ReviewRequest input) =>
        new()
        {
            RepositoryId = input.RepoName,
            PullRequestId = input.PullRequestId,
            Provider = input.Provider,
            EnqueuedAt = DateTime.UtcNow
        };

    private static async Task WriteQueuedResponseAsync(HttpResponseData httpResponse, HttpRequestData req)
    {
        httpResponse.StatusCode = HttpStatusCode.Accepted;
        httpResponse.Headers.Add("Location", $"https://{req.Url.Host}/api/review-status/{Guid.NewGuid()}");
        await httpResponse.WriteAsJsonAsync(new
        {
            message = "Review queued due to size.",
            status = "queued"
        });
    }

    private async Task<int> EstimateFileCountAsync(string repoName, int pullRequestId, string provider, CancellationToken cancellationToken)
    {
        try
        {
            if (!Enum.TryParse<ProviderType>(provider, true, out var providerEnum))
            {
                throw new ArgumentException($"Invalid provider type: {provider}");
            }

            var providerConfig = await _configService.GetProviderConfigAsync(providerEnum);

            var repoProvider = _repoProviderFactory.Create(providerConfig, repoName);

            var files = await repoProvider.GetChangedFilesAsync(providerConfig.ApiUrl, pullRequestId);
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