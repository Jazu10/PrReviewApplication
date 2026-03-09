using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AiPrReview.Core.Dto
{
    public class ChangedFile
    {
        public string FilePath { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty; // added, modified, deleted
        public string? Diff { get; set; }
        public string? Content { get; set; }
    }
}
