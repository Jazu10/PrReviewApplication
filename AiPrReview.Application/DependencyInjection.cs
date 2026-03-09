using AiPrReview.Application.Interfaces;
using AiPrReview.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AiPrReview.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IConfigurationService, ConfigurationService>();
        services.AddScoped<IPromptBuilder, PromptBuilder>();
        services.AddScoped<IAiResponseParser, AiResponseParser>();
        services.AddScoped<IReviewStrategyExecutor, ReviewStrategyExecutor>();
        services.AddScoped<ReviewOrchestrator>();

        return services;
    }
}

