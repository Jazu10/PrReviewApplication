using System.Text;
using System.Text.Json;
using Polly;
using AiPrReview.Core.Interfaces;

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

    public OpenAIClient(IDictionary<string, string> settings, IAsyncPolicy retryPolicy)
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {settings["ApiKey"]}");
        _model = settings["Model"];
        _temperature = double.Parse(settings["Temperature"]);
        _maxTokens = int.Parse(settings["MaxTokens"]);
        _systemPrompt = settings["SystemPrompt"];
        _url = settings["Url"];
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
            // Ensure this value is high enough in your settings (e.g., 4000)
            max_tokens = _maxTokens
        };

        // Use a fresh StringContent for each retry to avoid stream-read issues
        var response = await _retryPolicy.ExecuteAsync(async () =>
        {
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            return await _httpClient.PostAsync(_url, content, cancellationToken);
        });

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var choice = doc.RootElement.GetProperty("choices")[0];

        // Check why it stopped
        string finishReason = choice.GetProperty("finish_reason").GetString()!;
        if (finishReason == "length")
        {
            // This is your 'Partial String' culprit!
            // We log a warning so you know you need to increase MaxTokens.
            Console.WriteLine("WARNING: AI response was truncated because of MaxTokens limit.");
        }

        return choice.GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
    }
}