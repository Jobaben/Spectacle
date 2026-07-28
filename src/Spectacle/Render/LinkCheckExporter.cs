using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Spectacle.Render;

/// <summary>
/// Formats <see cref="LinkChecker"/> findings for headless output — a text
/// report (default) or structured JSON for an agent / CI step.
/// </summary>
public static class LinkCheckExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Build(IReadOnlyList<BrokenLink> broken, string sourcePath, bool json) =>
        json ? Json(broken, sourcePath) : Text(broken, sourcePath);

    private static string Text(IReadOnlyList<BrokenLink> broken, string sourcePath)
    {
        var sb = new StringBuilder();
        sb.Append(Path.GetFileName(sourcePath)).Append(" — links: ")
          .Append(broken.Count).AppendLine(" broken");
        foreach (var b in broken)
            sb.Append("  line ").Append(b.Line).Append("  '").Append(b.Target).Append("' — ")
              .AppendLine(b.Reason);
        return sb.ToString().TrimEnd('\n');
    }

    private static string Json(IReadOnlyList<BrokenLink> broken, string sourcePath)
    {
        var payload = new
        {
            source = sourcePath,
            brokenCount = broken.Count,
            broken,
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }
}
