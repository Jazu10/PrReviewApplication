using Polly;

namespace AiPrReview.Infrastructure.Policies;

public static class RetryPolicies
{
    public static IAsyncPolicy GetDefaultRetryPolicy() =>
        Policy.Handle<HttpRequestException>()
              .Or<TaskCanceledException>()
              .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
}