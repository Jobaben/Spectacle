using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Spectacle.Render;

/// <summary>
/// Formats a document outline (the heading tree from <see cref="OutlineExtractor"/>,
/// surfaced via <see cref="RenderResult.Outline"/>) for headless output — an
/// indented text tree (default) or structured JSON for navigation/tooling.
/// </summary>
public static class OutlineExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Build(IReadOnlyList<OutlineEntry> outline, string sourcePath, bool json) =>
        json ? Json(outline, sourcePath) : Text(outline, sourcePath);

    private static string Text(IReadOnlyList<OutlineEntry> outline, string sourcePath)
    {
        var sb = new StringBuilder();
        sb.Append(Path.GetFileName(sourcePath)).Append(" — outline: ")
          .Append(outline.Count).AppendLine(" heading(s)");
        foreach (var h in outline)
            sb.Append(new string(' ', 2 * (h.Level - 1) + 2))
              .Append(h.Text).Append("  (line ").Append(h.Line).AppendLine(")");
        return sb.ToString().TrimEnd('\n');
    }

    private static string Json(IReadOnlyList<OutlineEntry> outline, string sourcePath)
    {
        var payload = new
        {
            source = sourcePath,
            headingCount = outline.Count,
            headings = outline.Select(h => new { level = h.Level, text = h.Text, id = h.Id, line = h.Line }),
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }
}
