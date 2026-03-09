using AiPrReview.Core.Dto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AiPrReview.Application.Interfaces
{
    public interface IAiRequestBuilder
    {
        object BuildRequest(AiReviewRequest request, IDictionary<string, string> providerSettings);
    }
}
