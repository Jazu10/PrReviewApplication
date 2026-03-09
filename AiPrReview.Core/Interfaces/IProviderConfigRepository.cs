using AiPrReview.Core.Entities;
using AiPrReview.Core.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AiPrReview.Core.Interfaces
{
    public interface IProviderConfigRepository
    {
        Task<ProviderConfig?> GetByProviderAsync(ProviderType provider);
    }
}
