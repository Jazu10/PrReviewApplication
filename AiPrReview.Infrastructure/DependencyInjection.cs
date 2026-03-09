using AiPrReview.Application.Interfaces;
using AiPrReview.Core.Interfaces;
using AiPrReview.Infrastructure.Caching;
using AiPrReview.Infrastructure.Data;
using AiPrReview.Infrastructure.Factories;
using AiPrReview.Infrastructure.Policies;
using AiPrReview.Infrastructure.Repositories;
using AiPrReview.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiPrReview.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        var sqlConnectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(sqlConnectionString));

        services.AddScoped<IProviderConfigRepository, SqlProviderConfigRepository>();
        services.AddScoped<ILlmProviderRepository, SqlLlmProviderRepository>();

        services.AddMemoryCache();
        services.AddScoped<IConfigurationCache, MemoryConfigurationCache>();

        services.AddSingleton(RetryPolicies.GetDefaultRetryPolicy());

        services.AddScoped<IRepositoryProviderFactory, RepositoryProviderFactory>();
        services.AddScoped<ILlmClientFactory, LlmClientFactory>();

        services.AddSingleton<AiUsageLogger>();

        services.AddHttpClient();

        return services;
    }
}

