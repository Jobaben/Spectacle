using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Spectacle.Render;

/// <summary>Formats <see cref="TableChecker"/> issues — text (default) or JSON.</summary>
public static class TableCheckExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Build(IReadOnlyList<TableIssue> issues, string sourcePath, bool json) =>
        json ? Json(issues, sourcePath) : Text(issues, sourcePath);

    private static string Text(IReadOnlyList<TableIssue> issues, string sourcePath)
    {
        var sb = new StringBuilder();
        sb.Append(Path.GetFileName(sourcePath)).Append(" — tables: ")
          .Append(issues.Count).AppendLine(" issue(s)");
        foreach (var issue in issues)
            sb.Append("  line ").Append(issue.Line).Append("  ").AppendLine(issue.Message);
        return sb.ToString().TrimEnd('\n');
    }

    private static string Json(IReadOnlyList<TableIssue> issues, string sourcePath)
    {
        var payload = new { source = sourcePath, issueCount = issues.Count, issues };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }
}
