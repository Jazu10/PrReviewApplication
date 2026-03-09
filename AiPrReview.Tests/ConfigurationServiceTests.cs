using AiPrReview.Application.Exceptions;
using AiPrReview.Application.Interfaces;
using AiPrReview.Application.Services;
using AiPrReview.Core.Entities;
using AiPrReview.Core.Enums;
using AiPrReview.Core.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace AiPrReview.Tests;

public class ConfigurationServiceTests
{
    private sealed class InMemoryConfigurationCache : IConfigurationCache
    {
        private readonly IMemoryCache _memoryCache = new MemoryCache(new MemoryCacheOptions());

        public Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? absoluteExpirationRelativeToNow = null)
        {
            return _memoryCache.GetOrCreateAsync(key, async entry =>
            {
                if (absoluteExpirationRelativeToNow.HasValue)
                {
                    entry.AbsoluteExpirationRelativeToNow = absoluteExpirationRelativeToNow;
                }

                return await factory();
            })!;
        }
    }

    private sealed class StubProviderConfigRepository : IProviderConfigRepository
    {
        private readonly ProviderConfig? _config;

        public StubProviderConfigRepository(ProviderConfig? config)
        {
            _config = config;
        }

        public Task<ProviderConfig?> GetByProviderAsync(ProviderType provider) =>
            Task.FromResult(_config);
    }

    private sealed class StubLlmProviderRepository : ILlmProviderRepository
    {
        public Task<IEnumerable<LlmProvider>> GetActiveProvidersOrderedByPriorityAsync() =>
            Task.FromResult<IEnumerable<LlmProvider>>(Array.Empty<LlmProvider>());

        public Task<IDictionary<string, string>> GetSettingsAsync(int providerId) =>
            Task.FromResult<IDictionary<string, string>>(new Dictionary<string, string>());
    }

    [Fact]
    public async Task GetProviderConfigAsync_ThrowsWhenProviderMissing()
    {
        var service = new ConfigurationService(
            new StubProviderConfigRepository(null),
            new StubLlmProviderRepository(),
            new InMemoryConfigurationCache());

        await Assert.ThrowsAsync<ProviderNotFoundException>(() =>
            service.GetProviderConfigAsync(ProviderType.GitHub));
    }

    [Fact]
    public async Task GetProviderConfigAsync_ReturnsConfigWhenAvailable()
    {
        var expected = new ProviderConfig
        {
            Provider = ProviderType.GitHub,
            ApiUrl = "https://api.github.com",
            AccessToken = "token",
            ReviewStrategy = ReviewStrategy.Batch,
            PublishMode = PublishMode.Single
        };

        var service = new ConfigurationService(
            new StubProviderConfigRepository(expected),
            new StubLlmProviderRepository(),
            new InMemoryConfigurationCache());

        var config = await service.GetProviderConfigAsync(ProviderType.GitHub);

        Assert.Equal(expected.Provider, config.Provider);
        Assert.Equal(expected.ApiUrl, config.ApiUrl);
    }
}

