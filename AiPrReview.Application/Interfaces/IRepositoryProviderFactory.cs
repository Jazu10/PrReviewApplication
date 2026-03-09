using AiPrReview.Core.Entities;
using AiPrReview.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AiPrReview.Application.Interfaces
{
    public interface IRepositoryProviderFactory
    {
        IRepositoryProvider Create(ProviderConfig config, string repoName);
    }
}
