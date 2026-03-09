using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AiPrReview.Application.Exceptions
{
    public class NoActiveLlmProviderException : Exception
    {
        public NoActiveLlmProviderException() : base("No active LLM provider available.") { }
    }
}
