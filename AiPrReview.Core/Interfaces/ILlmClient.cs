using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AiPrReview.Core.Interfaces
{
    public interface ILlmClient
    {
        Task<string> ReviewAsync(string userPrompt, CancellationToken cancellationToken = default);
    }
}
