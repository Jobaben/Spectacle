using System;
using System.Collections.Generic;
using System.Linq;

namespace Spectacle.Render;

/// <summary>
/// The graded verdict for one document: every finding at its policy severity, the tallies, whether
/// the gate passed, and the context needed to trust the answer.
///
/// This is the shape a workflow consumes. It answers "may this document proceed?" in one boolean,
/// and it also answers the questions that make the boolean trustworthy: which checks were off,
/// how many findings an inline directive silenced, and what metadata the document declares about
/// its own provenance. A pass with six checks disabled is a different fact from a clean pass, and a
/// verdict that hid the difference would be worse than no verdict.
/// </summary>
public sealed record GateVerdict(
    string SourcePath,
    IReadOnlyList<GateFinding> Findings,
    GateSeverity FailOn,
    int BlockingCount,
    IReadOnlyList<string> SkippedChecks,
    int SuppressedCount,
    int ChecklistTotal,
    int ChecklistDone,
    IReadOnlyList<KeyValuePair<string, string>> Metadata,
    IReadOnlyList<string> AppliedGrades)
{
    /// <summary>Whether the document may proceed: no finding at or above the threshold.</summary>
    public bool Passed => BlockingCount == 0;

    /// <summary><c>pass</c> or <c>fail</c> — the token a workflow branches on.</summary>
    public string Status => Passed ? "pass" : "fail";

    public int ErrorCount => Findings.Count(f => f.Severity == GateSeverity.Error);
    public int WarningCount => Findings.Count(f => f.Severity == GateSeverity.Warning);
    public int InfoCount => Findings.Count(f => f.Severity == GateSeverity.Info);

    /// <summary>Findings at or above the threshold — the ones that have to be fixed.</summary>
    public IReadOnlyList<GateFinding> Blocking =>
        Findings.Where(f => f.Severity != GateSeverity.Info && f.Severity >= FailOn).ToList();

    /// <summary>
    /// Whether anything reduced this verdict's coverage — a disabled check or a suppressed finding.
    /// A caller showing a green badge should say so when this is true.
    /// </summary>
    public bool CoverageReduced => SkippedChecks.Count != 0 || SuppressedCount != 0;

    /// <summary>
    /// Grades <paramref name="report"/> under <paramref name="policy"/>. Every finding is carried,
    /// including advisories, so the verdict is the complete picture and the threshold alone decides
    /// what blocks.
    /// </summary>
    public static GateVerdict Compute(
        string sourcePath, ReviewReport report, GatePolicy policy, FrontMatterBlock header)
    {
        var findings = policy.Apply(FindingStream.All(report));
        return new GateVerdict(
            SourcePath: sourcePath,
            Findings: findings,
            FailOn: policy.FailOn,
            BlockingCount: findings.Count(f => policy.Blocks(f.Severity)),
            SkippedChecks: report.Skipped,
            SuppressedCount: report.SuppressedCount,
            ChecklistTotal: report.ChecklistTotal,
            ChecklistDone: report.ChecklistDone,
            Metadata: header.Metadata,
            AppliedGrades: policy.OverrideSummary);
    }

    /// <summary>
    /// The verdict for a document with no front-matter metadata to echo.
    /// </summary>
    public static GateVerdict Compute(string sourcePath, ReviewReport report, GatePolicy policy) =>
        Compute(sourcePath, report, policy, FrontMatterBlock.Absent);

    /// <summary>Findings grouped by severity, highest first, for a report that reads top-down.</summary>
    public IReadOnlyList<IGrouping<GateSeverity, GateFinding>> BySeverity =>
        Findings.GroupBy(f => f.Severity).OrderByDescending(g => g.Key).ToList();
}

/// <summary>
/// The verdict across a set of documents — a whole workflow output folder gated in one call. The
/// set passes only when every document in it passes.
/// </summary>
public sealed record GateBatch(IReadOnlyList<GateVerdict> Verdicts)
{
    public bool Passed => Verdicts.All(v => v.Passed);
    public string Status => Passed ? "pass" : "fail";
    public int BlockingCount => Verdicts.Sum(v => v.BlockingCount);
    public int ErrorCount => Verdicts.Sum(v => v.ErrorCount);
    public int WarningCount => Verdicts.Sum(v => v.WarningCount);
    public int InfoCount => Verdicts.Sum(v => v.InfoCount);

    /// <summary>The documents that failed, in input order — usually the only ones worth printing.</summary>
    public IReadOnlyList<GateVerdict> Failed => Verdicts.Where(v => !v.Passed).ToList();
}
