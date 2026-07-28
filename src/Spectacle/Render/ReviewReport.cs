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
    int SuppressedCount = 0,
    IReadOnlyList<TocIssue>? Toc = null,
    IReadOnlyList<UninformativeLink>? LinkText = null,
    IReadOnlyList<ProseFinding>? Prose = null,
    IReadOnlyList<FenceIssue>? FenceWarnings = null,
    IReadOnlyList<NumberingIssue>? Numbering = null,
    IReadOnlyList<BareUrl>? BareUrls = null,
    IReadOnlyList<HeadingNumberingIssue>? HeadingNumbering = null,
    IReadOnlyList<UndefinedReference>? LinkRefs = null,
    IReadOnlyList<UndefinedFootnote>? Footnotes = null,
    IReadOnlyList<FrontMatterFinding>? FrontMatterFindings = null,
    IReadOnlyList<AiArtifact>? AiArtifacts = null,
    IReadOnlyList<MermaidIssue>? Mermaid = null)
{
    /// <summary>Checks turned off for this verdict (project gate / <c>--only</c> / <c>--skip</c>),
    /// in canonical order, so the report can say a check was *off* rather than silently passing.</summary>
    public IReadOnlyList<string> Skipped => SkippedChecks ?? Array.Empty<string>();

    /// <summary>Table-of-contents drift findings (stale and missing entries).</summary>
    public IReadOnlyList<TocIssue> TocIssues => Toc ?? Array.Empty<TocIssue>();

    /// <summary>Links whose visible text says nothing about their destination.</summary>
    public IReadOnlyList<UninformativeLink> LinkTextIssues => LinkText ?? Array.Empty<UninformativeLink>();

    /// <summary>Advisory prose findings (hedging / vague language) — guidance, never gating.</summary>
    public IReadOnlyList<ProseFinding> ProseAdvisories => Prose ?? Array.Empty<ProseFinding>();

    /// <summary>Advisory fence findings (closed but untagged) — guidance, never gating.</summary>
    public IReadOnlyList<FenceIssue> FenceAdvisories => FenceWarnings ?? Array.Empty<FenceIssue>();

    /// <summary>Ordered lists whose numbering is out of sequence (gap, duplicate, or reorder).</summary>
    public IReadOnlyList<NumberingIssue> NumberingIssues => Numbering ?? Array.Empty<NumberingIssue>();

    /// <summary>Bare (auto-linked) URLs in prose that should be descriptive links.</summary>
    public IReadOnlyList<BareUrl> BareUrlIssues => BareUrls ?? Array.Empty<BareUrl>();

    /// <summary>Manually numbered headings whose section numbers are out of sequence.</summary>
    public IReadOnlyList<HeadingNumberingIssue> HeadingNumberingIssues => HeadingNumbering ?? Array.Empty<HeadingNumberingIssue>();

    /// <summary>Reference-style links whose label has no matching definition (renders as broken literal text).</summary>
    public IReadOnlyList<UndefinedReference> LinkRefIssues => LinkRefs ?? Array.Empty<UndefinedReference>();

    /// <summary>Footnote references with no matching definition (renders as broken literal text).</summary>
    public IReadOnlyList<UndefinedFootnote> FootnoteIssues => Footnotes ?? Array.Empty<UndefinedFootnote>();

    /// <summary>Defects in the document's YAML metadata header (missing/blank required keys, unclosed, misplaced).</summary>
    public IReadOnlyList<FrontMatterFinding> FrontMatterIssues => FrontMatterFindings ?? Array.Empty<FrontMatterFinding>();

    /// <summary>Residue of generation: unfilled template tokens, chat framing, truncation markers, placeholder targets.</summary>
    public IReadOnlyList<AiArtifact> AiArtifactIssues => AiArtifacts ?? Array.Empty<AiArtifact>();

    /// <summary>Mermaid diagrams that cannot be drawn, or that reach a screen reader undescribed.</summary>
    public IReadOnlyList<MermaidIssue> MermaidIssues => Mermaid ?? Array.Empty<MermaidIssue>();

    /// <summary>
    /// Count of advisory findings — hedging prose and untagged code fences. Surfaced in the
    /// verdict so the one command an agent runs sees this guidance too, but deliberately
    /// <em>excluded</em> from <see cref="IssueCount"/>: advisories are judgement calls, so they
    /// never change the pass/fail gate (the same report-don't-fail stance the standalone
    /// <c>--check-prose</c> and the fence <c>no-language</c> rule take).
    /// </summary>
    public int AdvisoryCount => ProseAdvisories.Count + FenceAdvisories.Count;

    /// <summary>Total problems across all enabled, non-suppressed checks (the checklist is informational).</summary>
    public int IssueCount =>
        Lint.Count + Structure.Count + Links.Count + Tables.Count + Fences.Count + Paths.Count
        + Duplication.Count + AltText.Count + LinkTextIssues.Count + EmphasisHeadings.Count
        + Sections.Count + TocIssues.Count + NumberingIssues.Count + BareUrlIssues.Count
        + HeadingNumberingIssues.Count + LinkRefIssues.Count + FootnoteIssues.Count
        + FrontMatterIssues.Count + AiArtifactIssues.Count + MermaidIssues.Count;

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
        ReviewChecks checks,
        IReadOnlyList<string>? requiredFrontMatter = null)
    {
        // Every prose check reads the *body*: the YAML metadata header is data, and a CommonMark
        // parser reads it as a setext heading, so leaving it in would put a phantom h2 in the
        // outline of every generated document. Strip blanks the header's lines rather than removing
        // them, so a finding's line number still points at the right line of the real file.
        var body = FrontMatter.Strip(content);
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

        var checklist = ChecklistAnalyzer.Analyze(body);
        return new ReviewReport(
            Lint: Run("lint", () => SpecLinter.Lint(body), f => f.Line),
            Structure: Run("structure", () => StructureChecker.Check(body), f => f.Line),
            Links: Run("links", () => LinkChecker.Check(body), b => b.Line),
            Tables: Run("tables", () => TableChecker.Check(body), t => t.Line),
            // Only the rendering defect (an unclosed fence) gates the verdict; a missing
            // language tag is advisory — it surfaces under FenceWarnings below (and the
            // dedicated --check-fences), never in the gating count.
            Fences: Run("fences",
                () => FenceChecker.Check(body).Where(f => f.Rule == FenceChecker.UnclosedRule).ToList(),
                f => f.Line),
            Paths: Run("paths", () => LinkPathChecker.Check(body, targetExists), p => p.Line),
            Duplication: Run("duplication", () => DuplicateBlockChecker.Check(body), d => d.Line),
            AltText: Run("alt-text", () => AltTextChecker.Check(body), a => a.Line),
            LinkText: Run("link-text", () => LinkTextChecker.Check(body), l => l.Line),
            EmphasisHeadings: Run("emphasis-heading", () => EmphasisHeadingChecker.Check(body), e => e.Line),
            // No template (empty list) means nothing is required, so this is a no-op —
            // preserving the verdict for specs reviewed without a .spectacle.json. A missing
            // section has no line, so inline line directives never apply; only the gate toggles it.
            Sections: checks.Has("sections")
                ? RequiredSectionsChecker.Check(body, requiredSections)
                : Array.Empty<MissingSection>(),
            ChecklistTotal: checklist.Count,
            ChecklistDone: checklist.Count(i => i.Checked),
            SkippedChecks: checks.Disabled,
            SuppressedCount: suppressed,
            // No TOC in the spec means no findings, so a spec without one is unaffected — the
            // same "enforced only when present" stance the section template uses.
            Toc: Run("toc", () => TocChecker.Check(body), t => t.Line),
            // A spec with no ordered lists (or only well-numbered ones) is unaffected.
            Numbering: Run("numbering", () => NumberingChecker.Check(body), n => n.Line),
            // A spec with no bare URLs is unaffected.
            BareUrls: Run("bare-urls", () => BareUrlChecker.Check(body), u => u.Line),
            // A spec that never numbers its headings is unaffected.
            HeadingNumbering: Run("heading-numbering", () => HeadingNumberingChecker.Check(body), h => h.Line),
            // A spec with no reference-style links is unaffected.
            LinkRefs: Run("link-refs", () => LinkRefChecker.Check(body), r => r.Line),
            // A spec with no footnotes is unaffected.
            Footnotes: Run("footnotes", () => FootnoteChecker.Check(body), f => f.Line),
            // The one check that reads the *raw* document: its subject is the metadata header the
            // others deliberately look past. With no required-key template it reports only genuine
            // malformations (unclosed, duplicated, misplaced), so a project that does not use front
            // matter is unaffected.
            FrontMatterFindings: Run("front-matter",
                () => FrontMatterChecker.Check(content, requiredFrontMatter ?? Array.Empty<string>()),
                f => f.Line),
            // A document written by hand carries none of this residue, so this is silent on one.
            AiArtifacts: Run("ai-artifacts", () => AiArtifactChecker.Check(body), a => a.Line),
            // A document with no ```mermaid fence is unaffected.
            Mermaid: Run("mermaid", () => MermaidChecker.Check(body), m => m.Line),
            // Advisories are guidance, not gating defects, so they are computed unconditionally —
            // independent of the gate selection (their ids never appear in --only/--skip) and never
            // counted in IssueCount. The fence advisory is the no-language rule the gate's fence
            // check deliberately drops (it keeps only the unclosed-fence rendering defect).
            Prose: ProseChecker.Check(body),
            FenceWarnings: FenceChecker.Check(body).Where(f => f.Rule == FenceChecker.NoLanguageRule).ToList());
    }
}
