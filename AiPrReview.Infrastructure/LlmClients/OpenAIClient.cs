using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Polly;
using AiPrReview.Core.Interfaces;
using AiPrReview.Infrastructure.Telemetry;

namespace AiPrReview.Infrastructure.LlmClients;

public class OpenAIClient : ILlmClient
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly double _temperature;
    private readonly int _maxTokens;
    private readonly string _systemPrompt;
    private readonly string _url;
    private readonly IAsyncPolicy _retryPolicy;
    private readonly AiUsageLogger _usageLogger;

    public OpenAIClient(
        IDictionary<string, string> settings,
        IAsyncPolicy retryPolicy,
        HttpClient httpClient,
        AiUsageLogger usageLogger)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {settings["ApiKey"]}");
        _model = settings["Model"];
        _temperature = double.Parse(settings["Temperature"]);
        _maxTokens = int.Parse(settings["MaxTokens"]);
        _systemPrompt = settings["SystemPrompt"];
        _url = settings["Url"];
        _retryPolicy = retryPolicy;
        _usageLogger = usageLogger;
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

        var stopwatch = Stopwatch.StartNew();

        var response = await _retryPolicy.ExecuteAsync(async () =>
        {
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            return await _httpClient.PostAsync(_url, content, cancellationToken);
        });

        stopwatch.Stop();
        _usageLogger.LogUsage("openai", 0, 0, stopwatch.Elapsed);

        // 👇 THE FIX: Read the actual OpenAI error before throwing
        if (!response.IsSuccessStatusCode)
        {
            var errorJson = await response.Content.ReadAsStringAsync(cancellationToken);
            // This will log exactly what is wrong (e.g., "context_length_exceeded" or "invalid_string")
            throw new HttpRequestException($"OpenAI API Error {(int)response.StatusCode}: {errorJson}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var choice = doc.RootElement.GetProperty("choices")[0];

        // Check why it stopped
        string finishReason = choice.GetProperty("finish_reason").GetString()!;
        if (finishReason == "length")
        {
            Console.WriteLine("WARNING: AI response was truncated because of MaxTokens limit.");
        }

        return choice.GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
    }
}