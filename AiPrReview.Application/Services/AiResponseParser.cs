using System.Text.RegularExpressions;
using AiPrReview.Application.Interfaces;
using AiPrReview.Core.Dto;

namespace AiPrReview.Application.Services;

public class AiResponseParser : IAiResponseParser
{
    public IReadOnlyList<ReviewComment> Parse(string aiResponse)
    {
        var comments = new List<ReviewComment>();
        if (string.IsNullOrWhiteSpace(aiResponse)) return comments;

        // Split by common file header patterns: "#### **", "### File:", or "#### **[FILE_PATH:"
        var blocks = Regex.Split(aiResponse, @"(?m)^#+\s*(?:\*\*|\[FILE_PATH:)?\s*");

        foreach (var block in blocks)
        {
            if (string.IsNullOrWhiteSpace(block) || (!block.Contains("**Issue:**") && !block.Contains("**Analysis:**")))
                continue;

            var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // 1. Extract File Path (Handles: [FILE_PATH: /path] or **/path**)
            var pathLine = lines[0].Replace("*", "").Replace("[FILE_PATH:", "").Replace("]", "").Trim();

            // 2. Extract Issue (Handles "**Issue:**" or "**Analysis:**")
            var issueMatch = Regex.Match(block, @"(?s)\*\*(?:Issue|Analysis):\*\*(.*?)\n\s*\*\*(?:Suggestion|Recommended Fix):\*\*");
            var issue = issueMatch.Success ? issueMatch.Groups[1].Value.Trim() : "";

            // Fallback for Issue if Regex failed
            if (string.IsNullOrEmpty(issue))
            {
                var parts = block.Split(new[] { "**Suggestion:**", "**Recommended Fix:**" }, StringSplitOptions.None);
                issue = parts[0].Substring(parts[0].IndexOf("**") > -1 ? parts[0].IndexOf("**") : 0).Trim();
            }

            // 3. Extract Suggestion
            var suggestionMatch = Regex.Match(block, @"(?s)\*\*(?:Suggestion|Recommended Fix):\*\*(.*)");
            var suggestion = suggestionMatch.Success ? suggestionMatch.Groups[1].Value.Trim() : "";

            // CLEANUP: Strip nested markdown markers and labels
            suggestion = suggestion
                .Replace("```csharp", "")
                .Replace("```", "")
                .Replace("**Suggestion:**", "")
                .Trim();

            // Remove trailing "---" if present
            issue = issue.TrimEnd('-', ' ');

            comments.Add(new ReviewComment
            {
                FilePath = pathLine,
                Issue = issue,
                Suggestion = suggestion
            });
        }
        return comments;
    }
}