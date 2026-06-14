using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Spectacle.Render;

/// <summary>Formats <see cref="LinkPathChecker"/> findings — text (default) or JSON.</summary>
public static class LinkPathCheckExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Build(IReadOnlyList<BrokenPath> broken, string sourcePath, bool json) =>
        json ? Json(broken, sourcePath) : Text(broken, sourcePath);

    private static string Text(IReadOnlyList<BrokenPath> broken, string sourcePath)
    {
        var sb = new StringBuilder();
        sb.Append(Path.GetFileName(sourcePath)).Append(" — paths: ")
          .Append(broken.Count).AppendLine(" broken");
        foreach (var b in broken)
            sb.Append("  line ").Append(b.Line).Append("  '").Append(b.Target).Append("' — ")
              .AppendLine(b.Reason);
        return sb.ToString().TrimEnd('\n');
    }

    private static string Json(IReadOnlyList<BrokenPath> broken, string sourcePath)
    {
        var payload = new { source = sourcePath, brokenCount = broken.Count, broken };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }
}
