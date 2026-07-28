using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Spectacle.Render;

/// <summary>
/// Formats <see cref="LinkRefChecker"/> results for headless output — a text summary (default)
/// or structured JSON for an agent / CI step.
/// </summary>
public static class LinkRefCheckExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Build(IReadOnlyList<UndefinedReference> refs, string sourcePath, bool json) =>
        json ? Json(refs, sourcePath) : Text(refs, sourcePath);

    private static string Text(IReadOnlyList<UndefinedReference> refs, string sourcePath)
    {
        var sb = new StringBuilder();
        sb.Append(Path.GetFileName(sourcePath)).Append(" — link references: ")
          .Append(refs.Count).AppendLine(" undefined reference(s)");
        foreach (var r in refs)
            sb.Append("  line ").Append(r.Line).Append("  ").Append(r.Reference)
              .Append(" — no definition for '").Append(r.Label).AppendLine("'");
        return sb.ToString().TrimEnd('\n');
    }

    private static string Json(IReadOnlyList<UndefinedReference> refs, string sourcePath)
    {
        var payload = new { source = sourcePath, count = refs.Count, references = refs };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }
}
