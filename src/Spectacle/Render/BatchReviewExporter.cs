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

    public static string Build(BatchReviewResult result, string root, bool json, bool markdown = false) =>
        markdown ? Markdown(result, root)
        : json ? Json(result, root)
        : Text(result, root);

    private static string Markdown(BatchReviewResult r, string root)
    {
        var sb = new StringBuilder();
        sb.Append("# Batch review: ")
          .AppendLine(Path.GetFileName(Path.TrimEndingDirectorySeparator(root)));
        sb.AppendLine();
        sb.Append("**").Append(r.FileCount).Append(" file(s) · ")
          .Append(r.FilesWithIssues).Append(" with issues · ")
          .Append(r.TotalIssues).AppendLine(" issue(s) total**");
        sb.AppendLine();

        foreach (var e in r.Entries)
        {
            sb.Append("## ").AppendLine(RelativePath(root, e.Path));
            sb.AppendLine();
            sb.AppendLine(ReviewReportExporter.Summary(e.Report));
            sb.AppendLine();
            ReviewReportExporter.AppendSections(sb, e.Report, "### ");
            if (e.Report.IssueCount == 0) sb.AppendLine("_No issues._").AppendLine();
            ReviewReportExporter.AppendAdvisories(sb, e.Report, "### ");
        }

        return sb.ToString().TrimEnd('\n');
    }

    private static string Text(BatchReviewResult r, string root)
    {
        var sb = new StringBuilder();
        sb.Append(Path.GetFileName(Path.TrimEndingDirectorySeparator(root))).Append(" — batch review: ")
          .Append(r.FileCount).Append(" file(s), ")
          .Append(r.FilesWithIssues).Append(" with issues, ")
          .Append(r.TotalIssues).AppendLine(" issue(s) total");

        foreach (var e in r.Entries)
        {
            sb.Append("  ").Append(RelativePath(root, e.Path)).Append(" — ")
              .Append(e.Report.IssueCount).Append(" issue(s)");
            if (e.Report.SuppressedCount > 0) sb.Append(", ").Append(e.Report.SuppressedCount).Append(" suppressed");
            if (e.Report.Skipped.Count > 0) sb.Append(" (skipped: ").Append(string.Join(", ", e.Report.Skipped)).Append(')');
            sb.AppendLine();
        }

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
                skippedChecks = e.Report.Skipped,
                suppressedCount = e.Report.SuppressedCount,
                lint = e.Report.Lint,
                structure = e.Report.Structure,
                links = e.Report.Links,
                tables = e.Report.Tables,
                fences = e.Report.Fences,
                paths = e.Report.Paths,
                duplication = e.Report.Duplication,
                altText = e.Report.AltText,
                linkText = e.Report.LinkTextIssues,
                emphasisHeadings = e.Report.EmphasisHeadings,
                sections = e.Report.Sections,
                toc = e.Report.TocIssues,
                advisoryCount = e.Report.AdvisoryCount,
                advisories = new { prose = e.Report.ProseAdvisories, fences = e.Report.FenceAdvisories },
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
