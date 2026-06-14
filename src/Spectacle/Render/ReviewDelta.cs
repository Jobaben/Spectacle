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
        return all;
    }
}
