using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Polly;
using AiPrReview.Core.Interfaces;
using AiPrReview.Infrastructure.Telemetry;

namespace AiPrReview.Infrastructure.LlmClients;

public class ClaudeClient : ILlmClient
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly double _temperature;
    private readonly int _maxTokens;
    private readonly string _systemPrompt;
    private readonly string _url;
    private readonly IAsyncPolicy _retryPolicy;
    private readonly AiUsageLogger _usageLogger;

    public ClaudeClient(
        IDictionary<string, string> settings,
        IAsyncPolicy retryPolicy,
        HttpClient httpClient,
        AiUsageLogger usageLogger)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.Add("x-api-key", settings["ApiKey"]);
        _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        _model = settings["Model"]; // e.g., "claude-3-opus-20240229"
        _temperature = double.Parse(settings["Temperature"]);
        _maxTokens = int.Parse(settings["MaxTokens"]);
        _systemPrompt = settings["SystemPrompt"];
        _url = settings["Url"]; // e.g., "https://api.anthropic.com/v1/messages"
        _retryPolicy = retryPolicy;
        _usageLogger = usageLogger;
    }

    public async Task<string> ReviewAsync(string userPrompt, CancellationToken cancellationToken = default)
    {
        // Claude API uses a different payload structure
        var payload = new
        {
            model = _model,
            system = _systemPrompt,
            messages = new[]
            {
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
        _usageLogger.LogUsage("claude", 0, 0, stopwatch.Elapsed);

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);

        // Claude returns content in a different structure
        return doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString()!;
    }
}