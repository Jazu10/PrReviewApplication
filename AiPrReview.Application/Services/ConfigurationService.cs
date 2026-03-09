using AiPrReview.Core.Entities;
using AiPrReview.Core.Interfaces;
using AiPrReview.Application.Interfaces;
using AiPrReview.Application.Exceptions;
using AiPrReview.Core.Enums;

namespace AiPrReview.Application.Services;

public class ConfigurationService : IConfigurationService
{
    private readonly IProviderConfigRepository _repoRepo;
    private readonly ILlmProviderRepository _llmRepo;
    private readonly IConfigurationCache _cache;

    public ConfigurationService(
        IProviderConfigRepository repoRepo,
        ILlmProviderRepository llmRepo,
        IConfigurationCache cache)
    {
        _repoRepo = repoRepo;
        _llmRepo = llmRepo;
        _cache = cache;
    }

    public async Task<ProviderConfig> GetProviderConfigAsync(ProviderType provider)
    {
        return await _cache.GetOrCreateAsync($"provider:{provider}",
            async () => await _repoRepo.GetByProviderAsync(provider)
                ?? throw new ProviderNotFoundException(provider.ToString()));
    }


    public async Task<(LlmProvider Provider, IDictionary<string, string> Settings)> GetActiveLlmProviderAsync()
    {
        return await _cache.GetOrCreateAsync("activeLlmProvider", async () =>
        {
            var providers = await _llmRepo.GetActiveProvidersOrderedByPriorityAsync();
            foreach (var provider in providers)
            {
                var settings = await _llmRepo.GetSettingsAsync(provider.Id);
                if (settings.ContainsKey("ApiKey"))
                    return (provider, settings);
            }
            throw new NoActiveLlmProviderException();
        }, TimeSpan.FromMinutes(10));
    }
}