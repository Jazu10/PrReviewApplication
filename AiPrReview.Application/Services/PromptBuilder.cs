using System.Text;
using AiPrReview.Application.Interfaces;
using AiPrReview.Core.Dto;

namespace AiPrReview.Application.Services;

public class PromptBuilder : IPromptBuilder
{
    private const string SystemInstructions = @"
Act as a Senior Full Stack Developer. Review the following PR and provide actionable feedback.
For every issue found, you MUST use the following format:

#### **[FILE_PATH]**
**Issue:** [Describe the bug, security risk, or code smell]
**Suggestion:** [Provide the specific code fix or optimization]

---
If a file is good, do not include it. Focus on Security, Performance, and Clean Code.";

    public string BuildBatchPrompt(PullRequestInfo prInfo, IReadOnlyList<ChangedFile> files)
    {
        var sb = new StringBuilder();
        sb.AppendLine(SystemInstructions);
        sb.AppendLine($"\nRepository: {prInfo.RepositoryName} | PR #{prInfo.Id}: {prInfo.Title}");
        sb.AppendLine($"Context: {prInfo.Description}\n");

        foreach (var file in files)
        {
            sb.AppendLine($"### File: {file.FilePath}");
            // Use Diff for context of change, Content for full file analysis
            sb.AppendLine("```diff");
            sb.AppendLine(file.Diff);
            sb.AppendLine("```");

            if (!string.IsNullOrEmpty(file.Content))
            {
                sb.AppendLine("Full File Content for reference:");
                sb.AppendLine("```");
                sb.AppendLine(file.Content);
                sb.AppendLine("```");
            }
        }
        return sb.ToString();
    }

    public string BuildFilePrompt(PullRequestInfo prInfo, ChangedFile file)
    {
        // Similar structure for single file...
        return $"{SystemInstructions}\n\nFile: {file.FilePath}\nDiff:\n{file.Diff}";
    }
}