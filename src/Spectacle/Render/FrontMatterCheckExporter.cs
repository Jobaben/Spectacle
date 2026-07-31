using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Spectacle.Checks;

namespace Spectacle.Render;

/// <summary>
/// Formats <see cref="FrontMatterChecker"/> results for headless output — text (default) or
/// structured JSON. The JSON also echoes the parsed metadata itself, so one call both validates
/// the header and hands the workflow the values it needs to route on.
/// </summary>
public static class FrontMatterCheckExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Build(
        IReadOnlyList<FrontMatterFinding> findings, FrontMatterBlock header, string sourcePath, bool json) =>
        json ? Json(findings, header, sourcePath) : Text(findings, header, sourcePath);

    private static string Text(
        IReadOnlyList<FrontMatterFinding> findings, FrontMatterBlock header, string sourcePath)
    {
        var sb = new StringBuilder();
        sb.Append(Path.GetFileName(sourcePath)).Append(" — front matter: ")
          .Append(findings.Count).AppendLine(" issue(s)");

        if (!header.Present) sb.AppendLine("  (no front matter)");
        else
            foreach (var (key, value) in header.Metadata)
                sb.Append("  ").Append(key).Append(": ").AppendLine(value.Length == 0 ? "(empty)" : value);

        foreach (var f in findings)
            sb.Append("  line ").Append(f.Line).Append("  [").Append(f.Rule).Append("] ").AppendLine(f.Message);

        return sb.ToString().TrimEnd('\n');
    }

    private static string Json(
        IReadOnlyList<FrontMatterFinding> findings, FrontMatterBlock header, string sourcePath)
    {
        var payload = new
        {
            source = sourcePath,
            count = findings.Count,
            frontMatter = new
            {
                present = header.Present,
                closed = header.Closed,
                startLine = header.StartLine,
                endLine = header.EndLine,
                keys = header.Entries.Select(e => new
                {
                    key = e.Key,
                    value = e.Value,
                    items = e.Items,
                    line = e.Line,
                    filled = e.HasValue,
                }),
            },
            findings,
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }
}
