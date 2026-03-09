using Microsoft.EntityFrameworkCore;
using AiPrReview.Core.Entities;
using AiPrReview.Core.Interfaces;
using AiPrReview.Infrastructure.Data;

namespace AiPrReview.Infrastructure.Repositories;

public class SqlRepositoryConfigRepository : IRepositoryConfigRepository
{
    private readonly AppDbContext _dbContext;

    public SqlRepositoryConfigRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<RepositoryConfig?> GetByIdAsync(Guid repositoryId)
    {
        return await _dbContext.Repositories
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == repositoryId);
    }
    public async Task<RepositoryConfig?> GetByNameAsync(string repositoryName)
    {
        return await _dbContext.Repositories
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Name == repositoryName);
    }
}