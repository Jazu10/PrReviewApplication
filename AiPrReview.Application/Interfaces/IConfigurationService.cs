using AiPrReview.Core.Entities;
using AiPrReview.Core.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AiPrReview.Application.Interfaces
{
    public interface IConfigurationService
    {
        Task<ProviderConfig> GetProviderConfigAsync(ProviderType provider);
        Task<(LlmProvider Provider, IDictionary<string, string> Settings)> GetActiveLlmProviderAsync();

    }
}
