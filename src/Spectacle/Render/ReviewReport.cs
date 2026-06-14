using System;
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
    IReadOnlyList<FenceIssue> Fences,
    IReadOnlyList<BrokenPath> Paths,
    int ChecklistTotal,
    int ChecklistDone)
{
    /// <summary>Total problems across all checks (the checklist is informational, not an issue).</summary>
    public int IssueCount =>
        Lint.Count + Structure.Count + Links.Count + Tables.Count + Fences.Count + Paths.Count;

    /// <summary>
    /// Review without a filesystem context: path existence is not checked (relative
    /// targets are assumed to resolve). Use the <see cref="Compute(string, Func{string, bool})"/>
    /// overload to validate relative link/image targets against disk.
    /// </summary>
    public static ReviewReport Compute(string content) => Compute(content, _ => true);

    public static ReviewReport Compute(string content, Func<string, bool> targetExists)
    {
        var checklist = ChecklistAnalyzer.Analyze(content);
        return new ReviewReport(
            Lint: SpecLinter.Lint(content),
            Structure: StructureChecker.Check(content),
            Links: LinkChecker.Check(content),
            Tables: TableChecker.Check(content),
            // Only the rendering defect (an unclosed fence) gates the verdict; a missing
            // language tag is advisory and surfaces solely under the dedicated --check-fences.
            Fences: FenceChecker.Check(content).Where(f => f.Rule == FenceChecker.UnclosedRule).ToList(),
            Paths: LinkPathChecker.Check(content, targetExists),
            ChecklistTotal: checklist.Count,
            ChecklistDone: checklist.Count(i => i.Checked));
    }
}
