using System;
using System.Collections.Generic;
using System.Linq;

namespace Spectacle.Render;

/// <summary>
/// Flattens a <see cref="ReviewReport"/>'s typed, per-check collections into one ordered stream of
/// <see cref="GateFinding"/>s.
///
/// This is the single place that knows how to turn every check's own finding record into a
/// (rule id, severity, line, message) tuple. Every consumer — SARIF, GitHub annotations, JUnit,
/// the fix brief, the terminal verdict, the reader's overlay — reads the stream instead of walking
/// the report itself, so adding a check means editing one method rather than six exporters.
///
/// Findings carry their catalogued default severity; <see cref="GatePolicy"/> re-grades them
/// afterwards. Keeping those steps separate means the stream stays a faithful description of what
/// was found, independent of what any one project chooses to block on.
/// </summary>
public static class FindingStream
{
    /// <summary>
    /// The gating findings — everything <see cref="ReviewReport.IssueCount"/> counts, in report
    /// order.
    /// </summary>
    public static IReadOnlyList<GateFinding> Gating(ReviewReport report) => GatingFindings(report).ToList();

    /// <summary>
    /// The advisory findings — guidance that never gates by default (hedging prose, untagged code
    /// fences), ordered by line.
    /// </summary>
    public static IReadOnlyList<GateFinding> Advisory(ReviewReport report) =>
        AdvisoryFindings(report).OrderBy(f => f.Line).ToList();

    /// <summary>
    /// Every finding, gating and advisory, ordered by line then by rule id — the reading order a
    /// verdict, an annotation set, or the reader's overlay wants.
    /// </summary>
    public static IReadOnlyList<GateFinding> All(ReviewReport report) =>
        GatingFindings(report).Concat(AdvisoryFindings(report))
            .OrderBy(f => f.Line)
            .ThenBy(f => f.RuleId, StringComparer.Ordinal)
            .ToList();

    // Emitted in the same order the report declares its checks, which is the order the SARIF log
    // and the text verdict have always used.
    private static IEnumerable<GateFinding> GatingFindings(ReviewReport r)
    {
        foreach (var f in r.Lint) yield return Make("lint", $"lint/{f.Rule}", f.Line, f.Message);
        foreach (var f in r.Structure) yield return Make("structure", $"structure/{f.Rule}", f.Line, f.Message);
        foreach (var b in r.Links) yield return Make("links", "links", b.Line, $"{b.Target}: {b.Reason}");
        foreach (var t in r.Tables) yield return Make("tables", "tables", t.Line, t.Message);
        foreach (var f in r.Fences) yield return Make("fences", $"fences/{f.Rule}", f.Line, f.Message);
        foreach (var p in r.Paths) yield return Make("paths", "paths", p.Line, $"{p.Target}: {p.Reason}");
        foreach (var d in r.Duplication)
            yield return Make("duplication", "duplication", d.Line, $"{d.Kind} duplicates line {d.FirstLine}");
        foreach (var a in r.AltText)
            yield return Make("alt-text", "alt-text", a.Line,
                $"image missing alt text: {(a.Target.Length == 0 ? "(no target)" : a.Target)}");
        foreach (var l in r.LinkTextIssues)
            yield return Make("link-text", $"link-text/{LinkTextChecker.RuleOf(l)}", l.Line, l.Reason);
        foreach (var e in r.EmphasisHeadings)
            yield return Make("emphasis-heading", "emphasis-heading", e.Line,
                $"emphasized line used as heading: '{e.Text}'");
        // A missing section is a document-level defect with no line of its own; anchoring it at
        // line 1 keeps every consumer's "findings have a location" contract intact.
        foreach (var s in r.Sections)
            yield return Make("sections", "sections", 1, $"missing required section: '{s.Required}'");
        foreach (var t in r.TocIssues) yield return Make("toc", $"toc/{t.Rule}", t.Line, t.Message);
        foreach (var n in r.NumberingIssues) yield return Make("numbering", $"numbering/{n.Rule}", n.Line, n.Message);
        foreach (var u in r.BareUrlIssues)
            yield return Make("bare-urls", $"bare-urls/{BareUrlChecker.BareUrlRule}", u.Line, $"bare URL: {u.Url}");
        foreach (var h in r.HeadingNumberingIssues)
            yield return Make("heading-numbering", $"heading-numbering/{h.Rule}", h.Line, h.Message);
        foreach (var lr in r.LinkRefIssues)
            yield return Make("link-refs", $"link-refs/{LinkRefChecker.UndefinedRule}", lr.Line,
                $"{lr.Reference}: no definition for '{lr.Label}'");
        foreach (var fn in r.FootnoteIssues)
            yield return Make("footnotes", $"footnotes/{FootnoteChecker.UndefinedRule}", fn.Line,
                $"footnote '[^{fn.Label}]' has no matching definition");
        foreach (var fm in r.FrontMatterIssues)
            yield return Make("front-matter", $"front-matter/{fm.Rule}", fm.Line, fm.Message);
        foreach (var a in r.AiArtifactIssues)
            yield return Make("ai-artifacts", $"ai-artifacts/{a.Rule}", a.Line, a.Message);
        foreach (var m in r.MermaidIssues)
            yield return Make("mermaid", $"mermaid/{m.Rule}", m.Line, m.Message);
    }

    private static IEnumerable<GateFinding> AdvisoryFindings(ReviewReport r)
    {
        foreach (var p in r.ProseAdvisories) yield return Make("prose", $"prose/{p.Rule}", p.Line, p.Message);
        foreach (var f in r.FenceAdvisories) yield return Make("fences", $"fences/{f.Rule}", f.Line, f.Message);
    }

    private static GateFinding Make(string checkId, string ruleId, int line, string message) =>
        new(checkId, ruleId, RuleCatalog.DefaultSeverityOf(ruleId), line, message);
}
