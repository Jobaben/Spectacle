using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Spectacle.Render;

/// <summary>
/// Formats <see cref="EmphasisHeadingChecker"/> results for headless output — text
/// (default) or structured JSON for an agent / CI step.
/// </summary>
public static class EmphasisHeadingCheckExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Build(IReadOnlyList<EmphasisHeading> findings, string sourcePath, bool json) =>
        json ? Json(findings, sourcePath) : Text(findings, sourcePath);

    private static string Text(IReadOnlyList<EmphasisHeading> findings, string sourcePath)
    {
        var sb = new StringBuilder();
        sb.Append(Path.GetFileName(sourcePath)).Append(" — emphasis headings: ")
          .Append(findings.Count).AppendLine(" emphasized line(s) used as a heading");
        foreach (var f in findings)
            sb.Append("  line ").Append(f.Line).Append("  '").Append(f.Text).AppendLine("'");
        return sb.ToString().TrimEnd('\n');
    }

    private static string Json(IReadOnlyList<EmphasisHeading> findings, string sourcePath)
    {
        var payload = new { source = sourcePath, count = findings.Count, headings = findings };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }
}
