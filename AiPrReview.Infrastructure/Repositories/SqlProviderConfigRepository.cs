using Microsoft.EntityFrameworkCore;
using AiPrReview.Core.Entities;
using AiPrReview.Core.Interfaces;
using AiPrReview.Infrastructure.Data;
using AiPrReview.Core.Enums;

namespace AiPrReview.Infrastructure.Repositories;

public class SqlProviderConfigRepository : IProviderConfigRepository
{
    private readonly AppDbContext _dbContext;

    public SqlProviderConfigRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ProviderConfig?> GetByProviderAsync(ProviderType provider)
    {
        return await _dbContext.ProviderConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Provider == provider && p.IsActive);
    }
}