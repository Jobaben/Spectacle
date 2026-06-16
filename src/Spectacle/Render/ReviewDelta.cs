using System.Collections.Generic;
using System.Linq;

namespace Spectacle.Render;

/// <summary>
/// A single review finding flattened across every check category, so findings from
/// different checks can be compared as one set. <see cref="Line"/> is the location in
/// whichever document the finding came from; it is deliberately excluded from identity.
/// </summary>
public sealed record DeltaFinding(string Category, string Rule, int Line, string Message);

/// <summary>
/// What changed in a spec's automated review between a baseline and a revision — the core
/// question in the write → review → revise loop with an AI agent. Findings are classified
/// <see cref="Fixed"/> (in the baseline, gone now), <see cref="New"/> (introduced by the
/// revision) and <see cref="Persisting"/> (present in both). Identity is
/// <c>(Category, Rule, Message)</c>, so a finding that merely moved to a different line counts
/// as persisting, not as one fixed plus one new.
/// </summary>
public sealed record ReviewDelta(
    IReadOnlyList<DeltaFinding> Fixed,
    IReadOnlyList<DeltaFinding> New,
    IReadOnlyList<DeltaFinding> Persisting,
    int BaselineChecklistDone,
    int BaselineChecklistTotal,
    int RevisedChecklistDone,
    int RevisedChecklistTotal)
{
    /// <summary>Total problems remaining in the revision (new + persisting).</summary>
    public int RemainingIssueCount => New.Count + Persisting.Count;

    public static ReviewDelta Compute(ReviewReport baseline, ReviewReport revised)
    {
        var before = Flatten(baseline);
        var after = Flatten(revised);

        // A multiset diff over line-insensitive identity: checks legitimately emit several
        // findings with the same (Category, Rule, Message) — two TODO placeholders, two empty
        // sections — so a set diff would miscount. Per identity, min(before, after) of the
        // occurrences persist; the surplus on each side is fixed (baseline) or new (revision).
        var beforeCounts = before.GroupBy(Identity).ToDictionary(g => g.Key, g => g.Count());
        var afterCounts = after.GroupBy(Identity).ToDictionary(g => g.Key, g => g.Count());

        var persisting = new List<DeltaFinding>();
        var newFindings = new List<DeltaFinding>();
        var fixedFindings = new List<DeltaFinding>();

        // Walk the revision in order: the first min(before, after) occurrences of each identity
        // are persisting (keeping their current lines); any beyond the baseline's count are new.
        var seenAfter = new Dictionary<(string, string, string), int>();
        foreach (var f in after)
        {
            var key = Identity(f);
            var budget = beforeCounts.GetValueOrDefault(key);
            var used = seenAfter.GetValueOrDefault(key);
            (used < budget ? persisting : newFindings).Add(f);
            seenAfter[key] = used + 1;
        }

        // Walk the baseline in order: occurrences beyond what the revision still carries are fixed.
        var seenBefore = new Dictionary<(string, string, string), int>();
        foreach (var f in before)
        {
            var key = Identity(f);
            var survived = afterCounts.GetValueOrDefault(key);
            var used = seenBefore.GetValueOrDefault(key);
            if (used >= survived) fixedFindings.Add(f);
            seenBefore[key] = used + 1;
        }

        return new ReviewDelta(
            fixedFindings, newFindings, persisting,
            baseline.ChecklistDone, baseline.ChecklistTotal,
            revised.ChecklistDone, revised.ChecklistTotal);
    }

    private static (string, string, string) Identity(DeltaFinding f) => (f.Category, f.Rule, f.Message);

    // A short, line-independent snippet of a duplicated block's text for the delta message:
    // the first non-empty line, trimmed and capped so identity stays stable and readable.
    private static string FirstLine(string text)
    {
        var line = text.Split('\n').FirstOrDefault(l => l.Trim().Length != 0)?.Trim() ?? string.Empty;
        return line.Length <= 60 ? line : line[..60] + "…";
    }

    /// <summary>Projects every check's findings into one ordered list of <see cref="DeltaFinding"/>.</summary>
    private static List<DeltaFinding> Flatten(ReviewReport r)
    {
        var all = new List<DeltaFinding>();
        all.AddRange(r.Lint.Select(f => new DeltaFinding("lint", f.Rule, f.Line, f.Message)));
        all.AddRange(r.Structure.Select(f => new DeltaFinding("structure", f.Rule, f.Line, f.Message)));
        all.AddRange(r.Links.Select(b => new DeltaFinding("links", "broken-link", b.Line, $"'{b.Target}' — {b.Reason}")));
        all.AddRange(r.Tables.Select(t => new DeltaFinding("tables", "table", t.Line, t.Message)));
        all.AddRange(r.Fences.Select(f => new DeltaFinding("fences", f.Rule, f.Line, f.Message)));
        all.AddRange(r.Paths.Select(p => new DeltaFinding("paths", "broken-path", p.Line, $"'{p.Target}' — {p.Reason}")));
        // Identity excludes line numbers, so duplication's message keys on the repeated
        // text (not the first-occurrence line, which shifts as the spec is edited).
        all.AddRange(r.Duplication.Select(d => new DeltaFinding("duplication", "duplicate-block", d.Line, $"[{d.Kind}] {FirstLine(d.Text)}")));
        all.AddRange(r.AltText.Select(a => new DeltaFinding("alt-text", "missing-alt", a.Line, $"'{a.Target}'")));
        all.AddRange(r.EmphasisHeadings.Select(e => new DeltaFinding("emphasis-heading", "emphasis-as-heading", e.Line, $"'{e.Text}'")));
        // A missing section has no line; identity is the section name.
        all.AddRange(r.Sections.Select(s => new DeltaFinding("sections", "missing-section", 0, $"'{s.Required}'")));
        all.AddRange(r.TocIssues.Select(t => new DeltaFinding("toc", t.Rule, t.Line, t.Message)));
        all.AddRange(r.NumberingIssues.Select(n => new DeltaFinding("numbering", n.Rule, n.Line, n.Message)));
        // Identity excludes line, so a bare URL keys on the URL text — a fix is the same URL gone.
        all.AddRange(r.BareUrlIssues.Select(u => new DeltaFinding("bare-urls", BareUrlChecker.BareUrlRule, u.Line, u.Url)));
        all.AddRange(r.HeadingNumberingIssues.Select(h => new DeltaFinding("heading-numbering", h.Rule, h.Line, h.Message)));
        // Identity excludes line, so an undefined reference keys on the failed label — a fix is
        // that label gone (defined, or the reference removed).
        all.AddRange(r.LinkRefIssues.Select(lr => new DeltaFinding("link-refs", LinkRefChecker.UndefinedRule, lr.Line, $"'{lr.Label}'")));
        all.AddRange(r.FootnoteIssues.Select(fn => new DeltaFinding("footnotes", FootnoteChecker.UndefinedRule, fn.Line, $"'[^{fn.Label}]'")));
        return all;
    }
}
