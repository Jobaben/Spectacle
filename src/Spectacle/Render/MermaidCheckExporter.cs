using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Spectacle.Checks;

namespace Spectacle.Render;

/// <summary>
/// Formats <see cref="MermaidChecker"/> results for headless output — text (default) or structured
/// JSON for an agent / CI step.
/// </summary>
public static class MermaidCheckExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Build(IReadOnlyList<MermaidIssue> issues, string sourcePath, bool json) =>
        json ? Json(issues, sourcePath) : Text(issues, sourcePath);

    private static string Text(IReadOnlyList<MermaidIssue> issues, string sourcePath)
    {
        var sb = new StringBuilder();
        sb.Append(Path.GetFileName(sourcePath)).Append(" — mermaid: ")
          .Append(issues.Count).AppendLine(" issue(s)");
        foreach (var issue in issues)
            sb.Append("  line ").Append(issue.Line).Append("  ")
              .Append(issue.Rule).Append("  ").AppendLine(issue.Message);
        return sb.ToString().TrimEnd('\n');
    }

    private static string Json(IReadOnlyList<MermaidIssue> issues, string sourcePath)
    {
        var payload = new { source = sourcePath, issueCount = issues.Count, issues };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }
}
