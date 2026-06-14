using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Spectacle.Render;

/// <summary>
/// Formats a <see cref="BatchReviewResult"/> — a per-file roll-up (default text) or
/// structured JSON carrying the full <see cref="ReviewReport"/> for each spec, so an
/// agent can act on every file's findings from one invocation.
/// </summary>
public static class BatchReviewExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Build(BatchReviewResult result, string root, bool json) =>
        json ? Json(result, root) : Text(result, root);

    private static string Text(BatchReviewResult r, string root)
    {
        var sb = new StringBuilder();
        sb.Append(Path.GetFileName(Path.TrimEndingDirectorySeparator(root))).Append(" — batch review: ")
          .Append(r.FileCount).Append(" file(s), ")
          .Append(r.FilesWithIssues).Append(" with issues, ")
          .Append(r.TotalIssues).AppendLine(" issue(s) total");

        foreach (var e in r.Entries)
            sb.Append("  ").Append(RelativePath(root, e.Path)).Append(" — ")
              .Append(e.Report.IssueCount).AppendLine(" issue(s)");

        return sb.ToString().TrimEnd('\n');
    }

    private static string Json(BatchReviewResult r, string root)
    {
        var payload = new
        {
            root,
            fileCount = r.FileCount,
            filesWithIssues = r.FilesWithIssues,
            totalIssues = r.TotalIssues,
            files = r.Entries.Select(e => new
            {
                source = e.Path,
                issueCount = e.Report.IssueCount,
                lint = e.Report.Lint,
                structure = e.Report.Structure,
                links = e.Report.Links,
                tables = e.Report.Tables,
                fences = e.Report.Fences,
                paths = e.Report.Paths,
                checklist = new
                {
                    total = e.Report.ChecklistTotal,
                    done = e.Report.ChecklistDone,
                    open = e.Report.ChecklistTotal - e.Report.ChecklistDone,
                },
            }),
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static string RelativePath(string root, string path)
    {
        try { return Path.GetRelativePath(root, path); }
        catch { return path; }
    }
}
