using System.Collections.Generic;
using System.Linq;

namespace Spectacle.Render;

/// <summary>
/// A single consolidated review of a spec: every automated check run at once.
/// One call, one verdict — what an AI agent or CI step needs to decide whether
/// a spec is ready, instead of invoking each check separately.
/// </summary>
public sealed record ReviewReport(
    IReadOnlyList<SpecLintFinding> Lint,
    IReadOnlyList<StructureFinding> Structure,
    IReadOnlyList<BrokenLink> Links,
    IReadOnlyList<TableIssue> Tables,
    int ChecklistTotal,
    int ChecklistDone)
{
    /// <summary>Total problems across all checks (the checklist is informational, not an issue).</summary>
    public int IssueCount => Lint.Count + Structure.Count + Links.Count + Tables.Count;

    public static ReviewReport Compute(string content)
    {
        var checklist = ChecklistAnalyzer.Analyze(content);
        return new ReviewReport(
            Lint: SpecLinter.Lint(content),
            Structure: StructureChecker.Check(content),
            Links: LinkChecker.Check(content),
            Tables: TableChecker.Check(content),
            ChecklistTotal: checklist.Count,
            ChecklistDone: checklist.Count(i => i.Checked));
    }
}
