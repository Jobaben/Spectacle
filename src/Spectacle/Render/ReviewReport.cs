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
    IReadOnlyList<DuplicateBlock> Duplication,
    IReadOnlyList<ImageWithoutAlt> AltText,
    IReadOnlyList<EmphasisHeading> EmphasisHeadings,
    IReadOnlyList<MissingSection> Sections,
    int ChecklistTotal,
    int ChecklistDone,
    IReadOnlyList<string>? SkippedChecks = null,
    int SuppressedCount = 0)
{
    /// <summary>Checks turned off for this verdict (project gate / <c>--only</c> / <c>--skip</c>),
    /// in canonical order, so the report can say a check was *off* rather than silently passing.</summary>
    public IReadOnlyList<string> Skipped => SkippedChecks ?? Array.Empty<string>();

    /// <summary>Total problems across all enabled, non-suppressed checks (the checklist is informational).</summary>
    public int IssueCount =>
        Lint.Count + Structure.Count + Links.Count + Tables.Count + Fences.Count + Paths.Count
        + Duplication.Count + AltText.Count + EmphasisHeadings.Count + Sections.Count;

    /// <summary>
    /// Review without a filesystem context: path existence is not checked (relative
    /// targets are assumed to resolve) and no spec template is enforced. Use the
    /// <see cref="Compute(string, Func{string, bool}, IReadOnlyList{string})"/> overload to
    /// validate relative targets against disk and enforce required sections.
    /// </summary>
    public static ReviewReport Compute(string content) => Compute(content, _ => true);

    /// <summary>
    /// Review that validates relative link/image targets against disk but enforces no spec
    /// template (the required-section check is a no-op).
    /// </summary>
    public static ReviewReport Compute(string content, Func<string, bool> targetExists) =>
        Compute(content, targetExists, Array.Empty<string>());

    /// <summary>
    /// The full consolidated review with every check enabled. <paramref name="requiredSections"/>
    /// declares the team's spec template (resolved from a <c>.spectacle.json</c> config by the
    /// caller); pass an empty list to enforce no template, matching the no-config behaviour.
    /// </summary>
    public static ReviewReport Compute(
        string content, Func<string, bool> targetExists, IReadOnlyList<string> requiredSections) =>
        Compute(content, targetExists, requiredSections, ReviewChecks.AllEnabled);

    /// <summary>
    /// The full consolidated review with a check selection. A check turned off by
    /// <paramref name="checks"/> contributes no findings (and is listed in
    /// <see cref="Skipped"/>); a finding silenced by an inline
    /// <c>spectacle-disable[-next]-line</c> directive in <paramref name="content"/> is dropped
    /// and tallied in <see cref="SuppressedCount"/>. Both keep the verdict honest — a disabled or
    /// suppressed finding stops gating without silently looking clean.
    /// </summary>
    public static ReviewReport Compute(
        string content,
        Func<string, bool> targetExists,
        IReadOnlyList<string> requiredSections,
        ReviewChecks checks)
    {
        var suppressions = InlineSuppressions.Parse(content);
        var suppressed = 0;

        // Runs a check only when enabled, then drops any finding an inline directive silences on
        // its line, counting each drop so the verdict can report what it muted.
        IReadOnlyList<T> Run<T>(string id, Func<IReadOnlyList<T>> run, Func<T, int> lineOf)
        {
            if (!checks.Has(id)) return Array.Empty<T>();
            var found = run();
            if (suppressions.IsEmpty || found.Count == 0) return found;

            var kept = new List<T>(found.Count);
            foreach (var f in found)
            {
                if (suppressions.IsSuppressed(id, lineOf(f))) suppressed++;
                else kept.Add(f);
            }
            return kept;
        }

        var checklist = ChecklistAnalyzer.Analyze(content);
        return new ReviewReport(
            Lint: Run("lint", () => SpecLinter.Lint(content), f => f.Line),
            Structure: Run("structure", () => StructureChecker.Check(content), f => f.Line),
            Links: Run("links", () => LinkChecker.Check(content), b => b.Line),
            Tables: Run("tables", () => TableChecker.Check(content), t => t.Line),
            // Only the rendering defect (an unclosed fence) gates the verdict; a missing
            // language tag is advisory and surfaces solely under the dedicated --check-fences.
            Fences: Run("fences",
                () => FenceChecker.Check(content).Where(f => f.Rule == FenceChecker.UnclosedRule).ToList(),
                f => f.Line),
            Paths: Run("paths", () => LinkPathChecker.Check(content, targetExists), p => p.Line),
            Duplication: Run("duplication", () => DuplicateBlockChecker.Check(content), d => d.Line),
            AltText: Run("alt-text", () => AltTextChecker.Check(content), a => a.Line),
            EmphasisHeadings: Run("emphasis-heading", () => EmphasisHeadingChecker.Check(content), e => e.Line),
            // No template (empty list) means nothing is required, so this is a no-op —
            // preserving the verdict for specs reviewed without a .spectacle.json. A missing
            // section has no line, so inline line directives never apply; only the gate toggles it.
            Sections: checks.Has("sections")
                ? RequiredSectionsChecker.Check(content, requiredSections)
                : Array.Empty<MissingSection>(),
            ChecklistTotal: checklist.Count,
            ChecklistDone: checklist.Count(i => i.Checked),
            SkippedChecks: checks.Disabled,
            SuppressedCount: suppressed);
    }
}
