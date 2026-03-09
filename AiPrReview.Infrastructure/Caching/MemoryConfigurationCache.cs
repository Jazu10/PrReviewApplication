using Microsoft.Extensions.Caching.Memory;
using AiPrReview.Application.Interfaces;

namespace AiPrReview.Infrastructure.Caching;

public class MemoryConfigurationCache : IConfigurationCache
{
    private readonly IMemoryCache _cache;

    public MemoryConfigurationCache(IMemoryCache cache)
    {
        _cache = cache;
    }

    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null)
    {
        if (_cache.TryGetValue(key, out var cachedObj) && cachedObj is T cached && cached is not null)
            return cached;

        var value = await factory();
        _cache.Set(key, value, expiry ?? TimeSpan.FromMinutes(5));
        return value;
    }
}