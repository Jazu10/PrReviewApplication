using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AiPrReview.Application.Exceptions
{
    public class ProviderNotFoundException : Exception
    {
        public ProviderNotFoundException(string provider)
            : base($"Provider with name: {provider} not found or inactive.") { }
    }
}
