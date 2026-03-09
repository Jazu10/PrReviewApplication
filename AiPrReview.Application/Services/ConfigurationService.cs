using AiPrReview.Core.Entities;
using AiPrReview.Core.Interfaces;
using AiPrReview.Application.Interfaces;
using AiPrReview.Application.Exceptions;

namespace AiPrReview.Application.Services;

public class ConfigurationService : IConfigurationService
{
    private readonly IRepositoryConfigRepository _repoRepo;
    private readonly ILlmProviderRepository _llmRepo;
    private readonly IReviewConfigRepository _reviewRepo;
    private readonly IConfigurationCache _cache;

    public ConfigurationService(
        IRepositoryConfigRepository repoRepo,
        ILlmProviderRepository llmRepo,
        IReviewConfigRepository reviewRepo,
        IConfigurationCache cache)
    {
        _repoRepo = repoRepo;
        _llmRepo = llmRepo;
        _reviewRepo = reviewRepo;
        _cache = cache;
    }

    public async Task<RepositoryConfig> GetRepositoryConfigAsync(Guid repositoryId)
    {
        return await _cache.GetOrCreateAsync($"repo:{repositoryId}",
            async () => await _repoRepo.GetByIdAsync(repositoryId)
                ?? throw new RepositoryNotFoundException(repositoryId.ToString()));
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

    public async Task<ReviewConfig?> GetReviewConfigAsync(Guid repositoryId)
    {
        return await _cache.GetOrCreateAsync($"reviewConfig:{repositoryId}",
            () => _reviewRepo.GetByRepositoryIdAsync(repositoryId));
    }

    public async Task<RepositoryConfig> GetRepositoryConfigByNameAsync(string repositoryName)
    {
        return await _cache.GetOrCreateAsync($"repo:name:{repositoryName}",
            async () => await _repoRepo.GetByNameAsync(repositoryName)
                ?? throw new RepositoryNotFoundException($"Repository '{repositoryName}' not found."));
    }
}