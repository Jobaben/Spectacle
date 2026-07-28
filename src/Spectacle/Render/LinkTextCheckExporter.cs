using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Spectacle.Render;

/// <summary>
/// Formats <see cref="LinkTextChecker"/> results for headless output — text (default)
/// or structured JSON for an agent / CI step.
/// </summary>
public static class LinkTextCheckExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Build(IReadOnlyList<UninformativeLink> links, string sourcePath, bool json) =>
        json ? Json(links, sourcePath) : Text(links, sourcePath);

    private static string Text(IReadOnlyList<UninformativeLink> links, string sourcePath)
    {
        var sb = new StringBuilder();
        sb.Append(Path.GetFileName(sourcePath)).Append(" — link text: ")
          .Append(links.Count).AppendLine(" uninformative link(s)");
        foreach (var l in links)
            sb.Append("  line ").Append(l.Line).Append("  ").AppendLine(l.Reason);
        return sb.ToString().TrimEnd('\n');
    }

    private static string Json(IReadOnlyList<UninformativeLink> links, string sourcePath)
    {
        var payload = new { source = sourcePath, uninformativeCount = links.Count, links };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }
}
