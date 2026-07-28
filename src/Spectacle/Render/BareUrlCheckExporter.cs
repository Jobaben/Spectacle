using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Spectacle.Render;

/// <summary>
/// Formats <see cref="BareUrlChecker"/> results for headless output — text (default) or
/// structured JSON for an agent / CI step.
/// </summary>
public static class BareUrlCheckExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Build(IReadOnlyList<BareUrl> urls, string sourcePath, bool json) =>
        json ? Json(urls, sourcePath) : Text(urls, sourcePath);

    private static string Text(IReadOnlyList<BareUrl> urls, string sourcePath)
    {
        var sb = new StringBuilder();
        sb.Append(Path.GetFileName(sourcePath)).Append(" — bare URLs: ")
          .Append(urls.Count).AppendLine(" bare URL(s)");
        foreach (var u in urls)
            sb.Append("  line ").Append(u.Line).Append("  ").AppendLine(u.Url);
        return sb.ToString().TrimEnd('\n');
    }

    private static string Json(IReadOnlyList<BareUrl> urls, string sourcePath)
    {
        var payload = new { source = sourcePath, count = urls.Count, urls };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }
}
