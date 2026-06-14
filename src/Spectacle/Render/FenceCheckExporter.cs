using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Spectacle.Render;

/// <summary>Formats <see cref="FenceChecker"/> issues — text (default) or JSON.</summary>
public static class FenceCheckExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Build(IReadOnlyList<FenceIssue> issues, string sourcePath, bool json) =>
        json ? Json(issues, sourcePath) : Text(issues, sourcePath);

    private static string Text(IReadOnlyList<FenceIssue> issues, string sourcePath)
    {
        var sb = new StringBuilder();
        sb.Append(Path.GetFileName(sourcePath)).Append(" — fences: ")
          .Append(issues.Count).AppendLine(" issue(s)");
        foreach (var issue in issues)
            sb.Append("  line ").Append(issue.Line).Append("  [").Append(issue.Rule).Append("] ")
              .AppendLine(issue.Message);
        return sb.ToString().TrimEnd('\n');
    }

    private static string Json(IReadOnlyList<FenceIssue> issues, string sourcePath)
    {
        var payload = new { source = sourcePath, issueCount = issues.Count, issues };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }
}
