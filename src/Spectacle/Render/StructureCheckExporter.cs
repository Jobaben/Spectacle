using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Spectacle.Render;

/// <summary>
/// Formats <see cref="StructureChecker"/> findings for headless output — text
/// (default) or structured JSON for an agent / CI step.
/// </summary>
public static class StructureCheckExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Build(IReadOnlyList<StructureFinding> findings, string sourcePath, bool json) =>
        json ? Json(findings, sourcePath) : Text(findings, sourcePath);

    private static string Text(IReadOnlyList<StructureFinding> findings, string sourcePath)
    {
        var sb = new StringBuilder();
        sb.Append(Path.GetFileName(sourcePath)).Append(" — structure: ")
          .Append(findings.Count).AppendLine(" finding(s)");
        foreach (var f in findings)
            sb.Append("  line ").Append(f.Line).Append("  [").Append(f.Rule).Append("] ")
              .AppendLine(f.Message);
        return sb.ToString().TrimEnd('\n');
    }

    private static string Json(IReadOnlyList<StructureFinding> findings, string sourcePath)
    {
        var payload = new
        {
            source = sourcePath,
            findingCount = findings.Count,
            findings,
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }
}
