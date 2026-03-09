using AiPrReview.Core.Dto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AiPrReview.Application.Interfaces
{
    public interface IPromptBuilder
    {
        string BuildBatchPrompt(PullRequestInfo prInfo, IReadOnlyList<ChangedFile> files);
        string BuildFilePrompt(PullRequestInfo prInfo, ChangedFile file);
    }
}
