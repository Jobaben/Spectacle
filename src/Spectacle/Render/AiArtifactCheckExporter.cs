using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Spectacle.Render;

/// <summary>
/// Formats <see cref="AiArtifactChecker"/> results for headless output — text (default) or
/// structured JSON for an agent / CI step. The text form groups by rule, because the fix for a
/// whole rule is usually one instruction ("drop the chat framing"), not one per line.
/// </summary>
public static class AiArtifactCheckExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Build(IReadOnlyList<AiArtifact> artifacts, string sourcePath, bool json) =>
        json ? Json(artifacts, sourcePath) : Text(artifacts, sourcePath);

    private static string Text(IReadOnlyList<AiArtifact> artifacts, string sourcePath)
    {
        var sb = new StringBuilder();
        sb.Append(Path.GetFileName(sourcePath)).Append(" — generation artifacts: ")
          .Append(artifacts.Count).AppendLine(" finding(s)");

        foreach (var group in artifacts.GroupBy(a => a.Rule).OrderBy(g => g.Key, System.StringComparer.Ordinal))
        {
            sb.Append("  ").Append(group.Key).Append(" (").Append(group.Count()).AppendLine("):");
            foreach (var a in group.OrderBy(a => a.Line))
                sb.Append("    line ").Append(a.Line).Append("  ").AppendLine(a.Excerpt);
        }

        return sb.ToString().TrimEnd('\n');
    }

    private static string Json(IReadOnlyList<AiArtifact> artifacts, string sourcePath)
    {
        var payload = new { source = sourcePath, count = artifacts.Count, artifacts };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }
}
