using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AiPrReview.Core.Dto
{
    public class PullRequestFileChange
    {
        public string FilePath { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Diff { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }
}
