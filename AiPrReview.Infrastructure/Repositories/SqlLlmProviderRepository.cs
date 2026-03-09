using Microsoft.EntityFrameworkCore;
using AiPrReview.Core.Entities;
using AiPrReview.Core.Interfaces;
using AiPrReview.Infrastructure.Data;

namespace AiPrReview.Infrastructure.Repositories;

public class SqlLlmProviderRepository : ILlmProviderRepository
{
    private readonly AppDbContext _dbContext;

    public SqlLlmProviderRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IEnumerable<LlmProvider>> GetActiveProvidersOrderedByPriorityAsync()
    {
        return await _dbContext.LlmProviders
            .Where(p => p.IsActive)
            .OrderBy(p => p.Priority)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<IDictionary<string, string>> GetSettingsAsync(int providerId)
    {
        return await _dbContext.LlmProviderSettings
            .Where(s => s.ProviderId == providerId)
            .AsNoTracking()
            .ToDictionaryAsync(s => s.Key, s => s.Value);
    }
}