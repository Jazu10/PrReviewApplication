using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AiPrReview.Core.Dto
{
    public class ReviewComment
    {
        public string FilePath { get; set; } = string.Empty;
        public int? Line { get; set; }
        public string Issue { get; set; } = string.Empty;
        public string Suggestion { get; set; } = string.Empty;
    }
}
