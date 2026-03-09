using AiPrReview.Core.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AiPrReview.Core.Interfaces
{
    public interface IRepositoryConfigRepository
    {
        Task<RepositoryConfig?> GetByIdAsync(Guid repositoryId);
        Task<RepositoryConfig?> GetByNameAsync(string repositoryName);
    }
}
