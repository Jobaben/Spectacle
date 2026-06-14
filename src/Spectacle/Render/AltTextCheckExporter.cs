using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Spectacle.Render;

/// <summary>
/// Formats <see cref="AltTextChecker"/> results for headless output — text (default)
/// or structured JSON for an agent / CI step.
/// </summary>
public static class AltTextCheckExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Build(IReadOnlyList<ImageWithoutAlt> images, string sourcePath, bool json) =>
        json ? Json(images, sourcePath) : Text(images, sourcePath);

    private static string Text(IReadOnlyList<ImageWithoutAlt> images, string sourcePath)
    {
        var sb = new StringBuilder();
        sb.Append(Path.GetFileName(sourcePath)).Append(" — alt text: ")
          .Append(images.Count).AppendLine(" image(s) missing alt text");
        foreach (var img in images)
            sb.Append("  line ").Append(img.Line).Append("  ")
              .AppendLine(img.Target.Length == 0 ? "(no target)" : img.Target);
        return sb.ToString().TrimEnd('\n');
    }

    private static string Json(IReadOnlyList<ImageWithoutAlt> images, string sourcePath)
    {
        var payload = new { source = sourcePath, missingCount = images.Count, images };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }
}
