using System.IO;
using System.Text;
using System.Text.Json;

namespace Spectacle.Render;

/// <summary>
/// Formats a <see cref="ReviewReport"/> — a grouped text summary (default) or
/// structured JSON with one array per check plus the checklist tally.
/// </summary>
public static class ReviewReportExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Build(ReviewReport report, string sourcePath, bool json) =>
        json ? Json(report, sourcePath) : Text(report, sourcePath);

    private static string Text(ReviewReport r, string sourcePath)
    {
        var sb = new StringBuilder();
        sb.Append(Path.GetFileName(sourcePath)).Append(" — review: ")
          .Append(r.IssueCount).AppendLine(" issue(s)");

        sb.Append("  lint (").Append(r.Lint.Count).AppendLine("):");
        foreach (var f in r.Lint)
            sb.Append("    line ").Append(f.Line).Append("  [").Append(f.Rule).Append("] ").AppendLine(f.Message);

        sb.Append("  structure (").Append(r.Structure.Count).AppendLine("):");
        foreach (var f in r.Structure)
            sb.Append("    line ").Append(f.Line).Append("  [").Append(f.Rule).Append("] ").AppendLine(f.Message);

        sb.Append("  links (").Append(r.Links.Count).AppendLine("):");
        foreach (var b in r.Links)
            sb.Append("    line ").Append(b.Line).Append("  '").Append(b.Target).Append("' — ").AppendLine(b.Reason);

        sb.Append("  tables (").Append(r.Tables.Count).AppendLine("):");
        foreach (var t in r.Tables)
            sb.Append("    line ").Append(t.Line).Append("  ").AppendLine(t.Message);

        sb.Append("  fences (").Append(r.Fences.Count).AppendLine("):");
        foreach (var f in r.Fences)
            sb.Append("    line ").Append(f.Line).Append("  [").Append(f.Rule).Append("] ").AppendLine(f.Message);

        sb.Append("  paths (").Append(r.Paths.Count).AppendLine("):");
        foreach (var p in r.Paths)
            sb.Append("    line ").Append(p.Line).Append("  '").Append(p.Target).Append("' — ").AppendLine(p.Reason);

        sb.Append("  checklist: ").Append(r.ChecklistDone).Append('/').Append(r.ChecklistTotal).Append(" complete");
        return sb.ToString();
    }

    private static string Json(ReviewReport r, string sourcePath)
    {
        var payload = new
        {
            source = sourcePath,
            issueCount = r.IssueCount,
            lint = r.Lint,
            structure = r.Structure,
            links = r.Links,
            tables = r.Tables,
            fences = r.Fences,
            paths = r.Paths,
            checklist = new { total = r.ChecklistTotal, done = r.ChecklistDone, open = r.ChecklistTotal - r.ChecklistDone },
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }
}
