using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AiPrReview.Core.Dto
{
    public record PullRequestInfo
    (
        int Id,
        string Title,
        string Description,
        string RepositoryName,
        string HeadSha,
        string BaseSha
    );
}
