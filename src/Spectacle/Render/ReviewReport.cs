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
    int ChecklistDone)
{
    /// <summary>Total problems across all checks (the checklist is informational, not an issue).</summary>
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
    /// The full consolidated review. <paramref name="requiredSections"/> declares the team's
    /// spec template (resolved from a <c>.spectacle.json</c> config by the caller); pass an
    /// empty list to enforce no template, matching the no-config behaviour.
    /// </summary>
    public static ReviewReport Compute(
        string content, Func<string, bool> targetExists, IReadOnlyList<string> requiredSections)
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
            Duplication: DuplicateBlockChecker.Check(content),
            AltText: AltTextChecker.Check(content),
            EmphasisHeadings: EmphasisHeadingChecker.Check(content),
            // No template (empty list) means nothing is required, so this is a no-op —
            // preserving the verdict for specs reviewed without a .spectacle.json.
            Sections: RequiredSectionsChecker.Check(content, requiredSections),
            ChecklistTotal: checklist.Count,
            ChecklistDone: checklist.Count(i => i.Checked));
    }
}
