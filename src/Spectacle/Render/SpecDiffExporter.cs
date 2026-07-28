using System.IO;
using System.Text;
using System.Text.Json;

namespace Spectacle.Render;

/// <summary>
/// Formats a <see cref="DiffResult"/> for headless output — a readable +/- text
/// report (default) or structured JSON.
/// </summary>
public static class SpecDiffExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Build(DiffResult diff, string sourcePath, string otherPath, bool json) =>
        json ? Json(diff, sourcePath, otherPath) : Text(diff, sourcePath, otherPath);

    private static string Text(DiffResult diff, string sourcePath, string otherPath)
    {
        var sb = new StringBuilder();
        sb.Append(Path.GetFileName(sourcePath)).Append(" vs ").Append(Path.GetFileName(otherPath))
          .Append(" — ").Append(diff.Added.Count).Append(" added, ")
          .Append(diff.Removed.Count).AppendLine(" removed");
        foreach (var e in diff.Removed)
            sb.Append("  - line ").Append(e.Line).Append("  ").AppendLine(FirstLine(e.Text));
        foreach (var e in diff.Added)
            sb.Append("  + line ").Append(e.Line).Append("  ").AppendLine(FirstLine(e.Text));
        return sb.ToString().TrimEnd('\n');
    }

    private static string Json(DiffResult diff, string sourcePath, string otherPath)
    {
        var payload = new
        {
            source = sourcePath,
            comparedTo = otherPath,
            addedCount = diff.Added.Count,
            removedCount = diff.Removed.Count,
            added = diff.Added,
            removed = diff.Removed,
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    // Collapse a multi-line block to its first line for the text report.
    private static string FirstLine(string text)
    {
        var nl = text.IndexOf('\n');
        return nl < 0 ? text : text[..nl] + " …";
    }
}
