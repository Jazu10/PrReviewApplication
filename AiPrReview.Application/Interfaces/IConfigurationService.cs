using AiPrReview.Core.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AiPrReview.Application.Interfaces
{
    public interface IConfigurationService
    {
        Task<RepositoryConfig> GetRepositoryConfigAsync(Guid repositoryId);
        Task<(LlmProvider Provider, IDictionary<string, string> Settings)> GetActiveLlmProviderAsync();
        Task<ReviewConfig?> GetReviewConfigAsync(Guid repositoryId);
        public Task<RepositoryConfig> GetRepositoryConfigByNameAsync(string repositoryName);

    }
}
