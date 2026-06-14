using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Spectacle.Render;

/// <summary>
/// Formats <see cref="ProseChecker"/> results for headless output — text (default) or
/// structured JSON for an agent / CI step. The check is advisory, so the output reads as
/// guidance ("consider revising") rather than a pass/fail verdict.
/// </summary>
public static class ProseCheckExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Build(IReadOnlyList<ProseFinding> findings, string sourcePath, bool json) =>
        json ? Json(findings, sourcePath) : Text(findings, sourcePath);

    private static string Text(IReadOnlyList<ProseFinding> findings, string sourcePath)
    {
        var sb = new StringBuilder();
        sb.Append(Path.GetFileName(sourcePath)).Append(" — prose: ")
          .Append(findings.Count).AppendLine(" vague/hedging phrase(s) [advisory]");
        foreach (var f in findings)
            sb.Append("  line ").Append(f.Line).Append("  [").Append(f.Rule).Append("] '")
              .Append(f.Phrase).AppendLine("'");
        return sb.ToString().TrimEnd('\n');
    }

    private static string Json(IReadOnlyList<ProseFinding> findings, string sourcePath)
    {
        var payload = new { source = sourcePath, count = findings.Count, findings };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }
}
