using System.Text;
using System.Text.Json;
using Polly;
using AiPrReview.Core.Interfaces;

namespace AiPrReview.Infrastructure.LlmClients;

public class GroqClient : ILlmClient
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly double _temperature;
    private readonly int _maxTokens;
    private readonly string _systemPrompt;
    private readonly string _url;
    private readonly IAsyncPolicy _retryPolicy;

    public GroqClient(IDictionary<string, string> settings, IAsyncPolicy retryPolicy)
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {settings["ApiKey"]}");

        _model = settings["Model"];
        _temperature = double.Parse(settings["Temperature"]);
        _maxTokens = int.Parse(settings["MaxTokens"]);
        _systemPrompt = settings["SystemPrompt"];
        _url = settings["Url"]; // e.g., "https://api.groq.com/openai/v1/chat/completions"
        _retryPolicy = retryPolicy;
    }

    public async Task<string> ReviewAsync(string userPrompt, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = _systemPrompt },
                new { role = "user", content = userPrompt }
            },
            temperature = _temperature,
            max_tokens = _maxTokens
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _retryPolicy.ExecuteAsync(async () =>
            await _httpClient.PostAsync(_url, content, cancellationToken));

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);

        // Groq API returns OpenAI-compatible response structure
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()!;
    }
}