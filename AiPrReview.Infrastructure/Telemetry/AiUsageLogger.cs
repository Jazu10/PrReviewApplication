using Microsoft.Extensions.Logging;

namespace AiPrReview.Infrastructure.Telemetry;

public class AiUsageLogger
{
    private readonly ILogger<AiUsageLogger> _logger;

    public AiUsageLogger(ILogger<AiUsageLogger> logger)
    {
        _logger = logger;
    }

    public void LogUsage(string provider, int promptTokens, int completionTokens, TimeSpan duration)
    {
        _logger.LogInformation("AI Usage: Provider={Provider}, PromptTokens={PromptTokens}, CompletionTokens={CompletionTokens}, Duration={Duration}ms",
            provider, promptTokens, completionTokens, duration.TotalMilliseconds);
    }
}