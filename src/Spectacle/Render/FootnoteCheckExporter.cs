using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Spectacle.Render;

/// <summary>
/// Formats <see cref="FootnoteChecker"/> results for headless output — a text summary (default)
/// or structured JSON for an agent / CI step.
/// </summary>
public static class FootnoteCheckExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Build(IReadOnlyList<UndefinedFootnote> footnotes, string sourcePath, bool json) =>
        json ? Json(footnotes, sourcePath) : Text(footnotes, sourcePath);

    private static string Text(IReadOnlyList<UndefinedFootnote> footnotes, string sourcePath)
    {
        var sb = new StringBuilder();
        sb.Append(Path.GetFileName(sourcePath)).Append(" — footnotes: ")
          .Append(footnotes.Count).AppendLine(" undefined footnote reference(s)");
        foreach (var f in footnotes)
            sb.Append("  line ").Append(f.Line).Append("  [^").Append(f.Label)
              .AppendLine("] — no matching definition");
        return sb.ToString().TrimEnd('\n');
    }

    private static string Json(IReadOnlyList<UndefinedFootnote> footnotes, string sourcePath)
    {
        var payload = new { source = sourcePath, count = footnotes.Count, footnotes };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }
}
