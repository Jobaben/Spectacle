using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Spectacle.Render;

/// <summary>
/// Formats a <see cref="ReviewReport"/> — a grouped text summary (default), structured JSON
/// with one array per check plus the checklist tally, or a Markdown report an agent or a
/// reviewer can read or paste straight into a pull request.
/// </summary>
public static class ReviewReportExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Build(ReviewReport report, string sourcePath, bool json, bool markdown = false) =>
        markdown ? Markdown(report, sourcePath)
        : json ? Json(report, sourcePath)
        : Text(report, sourcePath);

    private static string Text(ReviewReport r, string sourcePath)
    {
        var sb = new StringBuilder();
        sb.Append(Path.GetFileName(sourcePath)).Append(" — review: ")
          .Append(r.IssueCount).Append(" issue(s)");
        // Honesty: a suppressed finding or a skipped check stopped gating — say so, so a clean
        // verdict can't be confused with one that simply ran fewer checks.
        if (r.SuppressedCount > 0) sb.Append(", ").Append(r.SuppressedCount).Append(" suppressed");
        sb.AppendLine();
        if (r.Skipped.Count > 0) sb.Append("  skipped: ").AppendLine(string.Join(", ", r.Skipped));

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

        sb.Append("  duplication (").Append(r.Duplication.Count).AppendLine("):");
        foreach (var d in r.Duplication)
            sb.Append("    line ").Append(d.Line).Append("  [").Append(d.Kind)
              .Append("] duplicate of line ").Append(d.FirstLine).AppendLine();

        sb.Append("  alt-text (").Append(r.AltText.Count).AppendLine("):");
        foreach (var a in r.AltText)
            sb.Append("    line ").Append(a.Line).Append("  ")
              .AppendLine(a.Target.Length == 0 ? "(no target)" : a.Target);

        sb.Append("  link-text (").Append(r.LinkTextIssues.Count).AppendLine("):");
        foreach (var l in r.LinkTextIssues)
            sb.Append("    line ").Append(l.Line).Append("  ").AppendLine(l.Reason);

        sb.Append("  emphasis-headings (").Append(r.EmphasisHeadings.Count).AppendLine("):");
        foreach (var e in r.EmphasisHeadings)
            sb.Append("    line ").Append(e.Line).Append("  '").Append(e.Text).AppendLine("'");

        sb.Append("  sections (").Append(r.Sections.Count).AppendLine("):");
        foreach (var s in r.Sections)
            sb.Append("    missing  '").Append(s.Required).AppendLine("'");

        sb.Append("  toc (").Append(r.TocIssues.Count).AppendLine("):");
        foreach (var t in r.TocIssues)
            sb.Append("    line ").Append(t.Line).Append("  [").Append(t.Rule).Append("] ").AppendLine(t.Message);

        sb.Append("  numbering (").Append(r.NumberingIssues.Count).AppendLine("):");
        foreach (var n in r.NumberingIssues)
            sb.Append("    line ").Append(n.Line).Append("  [").Append(n.Rule).Append("] ").AppendLine(n.Message);

        // Advisories are guidance, not gate failures: shown after the issues, never in the count.
        sb.Append("  advisories (").Append(r.AdvisoryCount).AppendLine(") — not gating:");
        foreach (var f in r.FenceAdvisories)
            sb.Append("    line ").Append(f.Line).Append("  [fences/").Append(f.Rule).Append("] ").AppendLine(f.Message);
        foreach (var p in r.ProseAdvisories)
            sb.Append("    line ").Append(p.Line).Append("  [prose/").Append(p.Rule).Append("] ").AppendLine(p.Message);

        sb.Append("  checklist: ").Append(r.ChecklistDone).Append('/').Append(r.ChecklistTotal).Append(" complete");
        return sb.ToString();
    }

    private static string Json(ReviewReport r, string sourcePath)
    {
        var payload = new
        {
            source = sourcePath,
            issueCount = r.IssueCount,
            skippedChecks = r.Skipped,
            suppressedCount = r.SuppressedCount,
            lint = r.Lint,
            structure = r.Structure,
            links = r.Links,
            tables = r.Tables,
            fences = r.Fences,
            paths = r.Paths,
            duplication = r.Duplication,
            altText = r.AltText,
            linkText = r.LinkTextIssues,
            emphasisHeadings = r.EmphasisHeadings,
            sections = r.Sections,
            toc = r.TocIssues,
            numbering = r.NumberingIssues,
            // Advisories are reported but excluded from issueCount — guidance, not gate failures.
            advisoryCount = r.AdvisoryCount,
            advisories = new { prose = r.ProseAdvisories, fences = r.FenceAdvisories },
            checklist = new { total = r.ChecklistTotal, done = r.ChecklistDone, open = r.ChecklistTotal - r.ChecklistDone },
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static string Markdown(ReviewReport r, string sourcePath)
    {
        var sb = new StringBuilder();
        sb.Append("# Review: ").AppendLine(Path.GetFileName(sourcePath));
        sb.AppendLine();
        sb.AppendLine(Summary(r));
        sb.AppendLine();
        AppendSections(sb, r, "## ");
        if (r.IssueCount == 0) sb.AppendLine("No issues found.").AppendLine();
        AppendAdvisories(sb, r, "## ");
        sb.Append("**Checklist:** ").Append(r.ChecklistDone).Append(" / ")
          .Append(r.ChecklistTotal).Append(" complete");
        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>
    /// The one-line Markdown summary: the issue count plus an honest note of anything that
    /// stopped gating (suppressed findings, skipped checks), so a clean verdict can't be
    /// confused with one that simply ran fewer checks. Shared with the batch report.
    /// </summary>
    internal static string Summary(ReviewReport r)
    {
        var sb = new StringBuilder();
        sb.Append("**").Append(r.IssueCount).Append(r.IssueCount == 1 ? " issue" : " issues").Append("**");
        if (r.SuppressedCount > 0) sb.Append(" · ").Append(r.SuppressedCount).Append(" suppressed");
        if (r.Skipped.Count > 0) sb.Append(" · skipped: ").Append(string.Join(", ", r.Skipped));
        return sb.ToString();
    }

    /// <summary>
    /// Appends one Markdown subsection per check that found something, at the given heading
    /// prefix (<c>## </c> for a single file, <c>### </c> when nested under a per-file header in
    /// the batch report). Checks with no findings are omitted to keep the artifact readable.
    /// </summary>
    internal static void AppendSections(StringBuilder sb, ReviewReport r, string prefix)
    {
        Section(sb, prefix, "lint", r.Lint, f => $"`{f.Rule}` {f.Message}", f => f.Line);
        Section(sb, prefix, "structure", r.Structure, f => $"`{f.Rule}` {f.Message}", f => f.Line);
        Section(sb, prefix, "links", r.Links, b => $"`{b.Target}` — {b.Reason}", b => b.Line);
        Section(sb, prefix, "tables", r.Tables, t => t.Message, t => t.Line);
        Section(sb, prefix, "fences", r.Fences, f => $"`{f.Rule}` {f.Message}", f => f.Line);
        Section(sb, prefix, "paths", r.Paths, p => $"`{p.Target}` — {p.Reason}", p => p.Line);
        Section(sb, prefix, "duplication", r.Duplication,
            d => $"[{d.Kind}] duplicate of line {d.FirstLine}", d => d.Line);
        Section(sb, prefix, "alt-text", r.AltText,
            a => a.Target.Length == 0 ? "(no target)" : a.Target, a => a.Line);
        Section(sb, prefix, "link-text", r.LinkTextIssues, l => l.Reason, l => l.Line);
        Section(sb, prefix, "emphasis-heading", r.EmphasisHeadings, e => $"'{e.Text}'", e => e.Line);
        // A missing section has no line, so it renders without one.
        SectionNoLine(sb, prefix, "sections", r.Sections, s => $"missing '{s.Required}'");
        Section(sb, prefix, "toc", r.TocIssues, t => $"`{t.Rule}` {t.Message}", t => t.Line);
        Section(sb, prefix, "numbering", r.NumberingIssues, n => $"`{n.Rule}` {n.Message}", n => n.Line);
    }

    /// <summary>
    /// Appends the advisory subsection (untagged fences, hedging prose) at the given heading
    /// prefix, omitted entirely when there is nothing advisory to report. Advisories are guidance,
    /// so the heading says so and they are kept out of the issue sections above. Shared with the
    /// batch report so a folder review surfaces the same guidance per spec.
    /// </summary>
    internal static void AppendAdvisories(StringBuilder sb, ReviewReport r, string prefix)
    {
        if (r.AdvisoryCount == 0) return;
        sb.Append(prefix).Append("advisories (").Append(r.AdvisoryCount).Append(") — not gating")
          .AppendLine().AppendLine();
        foreach (var f in r.FenceAdvisories)
            sb.Append("- line ").Append(f.Line).Append(" — `fences/").Append(f.Rule).Append("` ").AppendLine(f.Message);
        foreach (var p in r.ProseAdvisories)
            sb.Append("- line ").Append(p.Line).Append(" — `prose/").Append(p.Rule).Append("` ").AppendLine(p.Message);
        sb.AppendLine();
    }

    private static void Section<T>(
        StringBuilder sb, string prefix, string name, IReadOnlyList<T> items,
        Func<T, string> render, Func<T, int> lineOf)
    {
        if (items.Count == 0) return;
        sb.Append(prefix).Append(name).Append(" (").Append(items.Count).Append(')').AppendLine().AppendLine();
        foreach (var it in items)
            sb.Append("- line ").Append(lineOf(it)).Append(" — ").AppendLine(render(it));
        sb.AppendLine();
    }

    private static void SectionNoLine<T>(
        StringBuilder sb, string prefix, string name, IReadOnlyList<T> items, Func<T, string> render)
    {
        if (items.Count == 0) return;
        sb.Append(prefix).Append(name).Append(" (").Append(items.Count).Append(')').AppendLine().AppendLine();
        foreach (var it in items)
            sb.Append("- ").AppendLine(render(it));
        sb.AppendLine();
    }
}
