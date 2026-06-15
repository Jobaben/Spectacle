using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Spectacle.Render;

/// <summary>One table-of-contents inconsistency, with its 1-based line.</summary>
public sealed record TocIssue(string Rule, int Line, string Message, string Anchor);

/// <summary>
/// Validates a document's table of contents against its actual headings — the drift an
/// AI agent introduces when it adds, renames, or removes a section but forgets to update
/// the TOC. Two defects nothing else catches: a TOC entry pointing at a heading that no
/// longer exists (<c>stale-toc-entry</c>) and a section heading the TOC never lists
/// (<c>missing-from-toc</c>). The check is a no-op when the spec has no TOC, so a spec that
/// never declared one is unaffected. It uses the same Markdig auto-identifier behaviour as
/// <see cref="LinkChecker"/>, so the heading slugs matched here are the ones the viewer emits.
/// </summary>
public static class TocChecker
{
    /// <summary>A TOC entry whose anchor matches no heading in the document.</summary>
    public const string StaleEntryRule = "stale-toc-entry";

    /// <summary>A section heading (at a level the TOC covers) with no entry in the TOC.</summary>
    public const string MissingEntryRule = "missing-from-toc";

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseAutoIdentifiers()
        .UseGenericAttributes()
        .UsePreciseSourceLocation()
        .Build();

    // The headings that introduce a table of contents. Matched case-insensitively against
    // the heading's trimmed plain text.
    private static readonly HashSet<string> TocTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        "table of contents", "contents", "toc",
    };

    private sealed record HeadingInfo(int Level, string Id, string Text, int Line);
    private sealed record TocEntry(string Anchor, string Text, int Line);

    public static IReadOnlyList<TocIssue> Check(string? markdown)
    {
        var document = Markdown.Parse(markdown ?? string.Empty, Pipeline);

        var headings = document.Descendants<HeadingBlock>()
            .Select(h => new HeadingInfo(h.Level, h.TryGetAttributes()?.Id ?? string.Empty, PlainText(h.Inline), h.Line + 1))
            .ToList();

        var tocIndex = headings.FindIndex(h => TocTitles.Contains(h.Text.Trim()));
        if (tocIndex < 0) return Array.Empty<TocIssue>();
        var tocHeading = headings[tocIndex];

        // The TOC section runs from its heading to the next heading at the same or a higher
        // level (the standard "section ends where a sibling/ancestor begins" rule).
        var next = headings.Skip(tocIndex + 1).FirstOrDefault(h => h.Level <= tocHeading.Level);
        var sectionEnd = next?.Line ?? int.MaxValue;

        var entries = document.Descendants<LinkInline>()
            .Where(l => !l.IsImage && (l.Url ?? string.Empty).StartsWith('#')
                        && l.Line + 1 > tocHeading.Line && l.Line + 1 < sectionEnd)
            .Select(l => new TocEntry((l.Url ?? string.Empty)[1..], PlainText(l), l.Line + 1))
            .ToList();

        // A "Contents" heading with no anchor links beneath it is not a table of contents.
        if (entries.Count == 0) return Array.Empty<TocIssue>();

        var headingIds = headings
            .Where(h => h.Id.Length > 0)
            .Select(h => h.Id)
            .ToHashSet(StringComparer.Ordinal);

        var issues = new List<TocIssue>();

        // stale-toc-entry: the TOC points at a heading that does not exist (removed or renamed).
        foreach (var e in entries)
            if (e.Anchor.Length == 0 || !headingIds.Contains(e.Anchor))
                issues.Add(new TocIssue(
                    StaleEntryRule, e.Line,
                    $"TOC entry '{e.Text}' points to '#{e.Anchor}', which matches no heading", $"#{e.Anchor}"));

        // The depth the TOC actually covers is inferred from the entries that do resolve, so a
        // deep subsection the TOC never meant to list is not flagged as missing.
        var linked = entries.Select(e => e.Anchor).ToHashSet(StringComparer.Ordinal);
        var coveredLevels = headings
            .Where(h => h.Id.Length > 0 && linked.Contains(h.Id))
            .Select(h => h.Level)
            .ToHashSet();

        // missing-from-toc: a body heading (after the TOC, at a covered level) the TOC omits.
        foreach (var h in headings)
            if (h.Line >= sectionEnd && h.Id.Length > 0
                && coveredLevels.Contains(h.Level) && !linked.Contains(h.Id))
                issues.Add(new TocIssue(
                    MissingEntryRule, h.Line,
                    $"heading '{h.Text}' has no entry in the table of contents", $"#{h.Id}"));

        return issues.OrderBy(i => i.Line).ToList();
    }

    // The visible text of an inline container: literals and inline code joined, trimmed.
    // Mirrors how a reader sees a heading or link label, ignoring emphasis/markup nodes.
    private static string PlainText(ContainerInline? inline)
    {
        if (inline is null) return string.Empty;
        var sb = new StringBuilder();
        foreach (var node in inline.Descendants())
        {
            if (node is LiteralInline lit) sb.Append(lit.Content.ToString());
            else if (node is CodeInline code) sb.Append(code.Content);
        }
        return sb.ToString().Trim();
    }
}
