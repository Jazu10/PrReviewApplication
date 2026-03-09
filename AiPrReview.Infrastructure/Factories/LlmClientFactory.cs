using Polly;
using AiPrReview.Core.Interfaces;
using AiPrReview.Application.Interfaces;
using AiPrReview.Infrastructure.LlmClients;

namespace AiPrReview.Infrastructure.Factories;

public class LlmClientFactory : ILlmClientFactory
{
    private readonly IAsyncPolicy _retryPolicy;

    public LlmClientFactory(IAsyncPolicy retryPolicy)
    {
        _retryPolicy = retryPolicy;
    }

    public ILlmClient Create(string providerName, IDictionary<string, string> settings)
    {
        return providerName.ToLower() switch
        {
            "openai" => new OpenAIClient(settings, _retryPolicy),
            "groq" => new GroqClient(settings, _retryPolicy),
            "claude" => new ClaudeClient(settings, _retryPolicy),
            _ => throw new NotSupportedException($"Provider {providerName} not supported.")
        };
    }
}