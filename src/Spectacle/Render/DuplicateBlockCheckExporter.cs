using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Spectacle.Render;

/// <summary>
/// Formats <see cref="DuplicateBlockChecker"/> results for headless output — text
/// (default) or structured JSON for an agent / CI step.
/// </summary>
public static class DuplicateBlockCheckExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Build(IReadOnlyList<DuplicateBlock> duplicates, string sourcePath, bool json) =>
        json ? Json(duplicates, sourcePath) : Text(duplicates, sourcePath);

    private static string Text(IReadOnlyList<DuplicateBlock> duplicates, string sourcePath)
    {
        var sb = new StringBuilder();
        sb.Append(Path.GetFileName(sourcePath)).Append(" — duplication: ")
          .Append(duplicates.Count).AppendLine(" repeated block(s)");
        foreach (var d in duplicates)
            sb.Append("  line ").Append(d.Line).Append("  [").Append(d.Kind)
              .Append("] duplicate of line ").Append(d.FirstLine).Append("  ")
              .AppendLine(FirstLine(d.Text));
        return sb.ToString().TrimEnd('\n');
    }

    private static string Json(IReadOnlyList<DuplicateBlock> duplicates, string sourcePath)
    {
        var payload = new { source = sourcePath, duplicateCount = duplicates.Count, duplicates };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    // Collapse a multi-line block to its first line for the text report.
    private static string FirstLine(string text)
    {
        var nl = text.IndexOf('\n');
        return nl < 0 ? text : text[..nl] + " …";
    }
}
