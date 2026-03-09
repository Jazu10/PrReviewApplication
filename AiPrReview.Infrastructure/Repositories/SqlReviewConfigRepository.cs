using Microsoft.EntityFrameworkCore;
using AiPrReview.Core.Entities;
using AiPrReview.Core.Interfaces;
using AiPrReview.Infrastructure.Data;

namespace AiPrReview.Infrastructure.Repositories;

public class SqlReviewConfigRepository : IReviewConfigRepository
{
    private readonly AppDbContext _dbContext;

    public SqlReviewConfigRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ReviewConfig?> GetByRepositoryIdAsync(Guid repositoryId)
    {
        return await _dbContext.ReviewConfigurations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.RepositoryId == repositoryId);
    }
}