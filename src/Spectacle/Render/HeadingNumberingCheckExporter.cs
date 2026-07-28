using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Spectacle.Render;

/// <summary>
/// Formats <see cref="HeadingNumberingChecker"/> results for headless output — text (default) or
/// structured JSON for an agent / CI step.
/// </summary>
public static class HeadingNumberingCheckExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Build(IReadOnlyList<HeadingNumberingIssue> issues, string sourcePath, bool json) =>
        json ? Json(issues, sourcePath) : Text(issues, sourcePath);

    private static string Text(IReadOnlyList<HeadingNumberingIssue> issues, string sourcePath)
    {
        var sb = new StringBuilder();
        sb.Append(Path.GetFileName(sourcePath)).Append(" — heading numbering: ")
          .Append(issues.Count).AppendLine(" heading run(s) out of sequence");
        foreach (var i in issues)
            sb.Append("  line ").Append(i.Line).Append("  [").Append(i.Rule).Append("] ").AppendLine(i.Message);
        return sb.ToString().TrimEnd('\n');
    }

    private static string Json(IReadOnlyList<HeadingNumberingIssue> issues, string sourcePath)
    {
        var payload = new { source = sourcePath, count = issues.Count, issues };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }
}
