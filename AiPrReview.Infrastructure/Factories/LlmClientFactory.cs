using System.Net.Http;
using Polly;
using AiPrReview.Core.Interfaces;
using AiPrReview.Application.Interfaces;
using AiPrReview.Infrastructure.LlmClients;
using AiPrReview.Infrastructure.Telemetry;

namespace AiPrReview.Infrastructure.Factories;

public class LlmClientFactory : ILlmClientFactory
{
    private readonly IAsyncPolicy _retryPolicy;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AiUsageLogger _usageLogger;

    public LlmClientFactory(
        IAsyncPolicy retryPolicy,
        IHttpClientFactory httpClientFactory,
        AiUsageLogger usageLogger)
    {
        _retryPolicy = retryPolicy;
        _httpClientFactory = httpClientFactory;
        _usageLogger = usageLogger;
    }

    public ILlmClient Create(string providerName, IDictionary<string, string> settings)
    {
        return providerName.ToLower() switch
        {
            "openai" => new OpenAIClient(settings, _retryPolicy, _httpClientFactory.CreateClient("openai-llm"), _usageLogger),
            "groq" => new GroqClient(settings, _retryPolicy, _httpClientFactory.CreateClient("groq-llm"), _usageLogger),
            "claude" => new ClaudeClient(settings, _retryPolicy, _httpClientFactory.CreateClient("claude-llm"), _usageLogger),
            _ => throw new NotSupportedException($"Provider {providerName} not supported.")
        };
    }
}