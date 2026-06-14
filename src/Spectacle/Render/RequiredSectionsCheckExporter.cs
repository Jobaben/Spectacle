using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Spectacle.Render;

/// <summary>
/// Formats <see cref="RequiredSectionsChecker"/> results for headless output — text
/// (default) or structured JSON for an agent / CI step.
/// </summary>
public static class RequiredSectionsCheckExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Build(
        IReadOnlyList<MissingSection> missing, int requiredCount, string sourcePath, bool json) =>
        json ? Json(missing, requiredCount, sourcePath) : Text(missing, requiredCount, sourcePath);

    private static string Text(IReadOnlyList<MissingSection> missing, int requiredCount, string sourcePath)
    {
        var sb = new StringBuilder();
        sb.Append(Path.GetFileName(sourcePath)).Append(" — required sections: ")
          .Append(requiredCount - missing.Count).Append('/').Append(requiredCount)
          .Append(" present, ").Append(missing.Count).AppendLine(" missing");
        foreach (var m in missing)
            sb.Append("  missing: '").Append(m.Required).AppendLine("'");
        return sb.ToString().TrimEnd('\n');
    }

    private static string Json(IReadOnlyList<MissingSection> missing, int requiredCount, string sourcePath)
    {
        var payload = new
        {
            source = sourcePath,
            requiredCount,
            presentCount = requiredCount - missing.Count,
            missingCount = missing.Count,
            missing,
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }
}
