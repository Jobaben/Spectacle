using System.Collections.Generic;
using System.Linq;
using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Spectacle.Render;

/// <summary>A paragraph used as a fake heading — its emphasized text and 1-based line.</summary>
public sealed record EmphasisHeading(string Text, int Line);

/// <summary>
/// Structure check: reports a paragraph that is nothing but a single bold or italic run
/// (<c>**Overview**</c> / <c>_Goals_</c> on its own line) — the fake heading an AI agent
/// emits instead of a real <c>## Overview</c>. It looks like a heading but is not one, so
/// it is invisible to every heading-based tool here: <see cref="OutlineExtractor"/> never
/// lists it, <see cref="RequiredSectionsChecker"/> never counts it as a present section,
/// and <see cref="StructureChecker"/> cannot reason about its level. Catching it keeps the
/// whole heading toolchain trustworthy.
///
/// Mirrors the markdownlint MD036 rule: a single-line paragraph whose entire content is one
/// emphasis run and that does not end in sentence punctuation (so an emphasized *sentence*
/// — <c>**Note this carefully.**</c> — is left alone). Only top-level paragraphs are
/// considered; an emphasized list item (<c>- **Term**</c>) or blockquote line is a
/// legitimate construct, not a heading.
/// </summary>
public static class EmphasisHeadingChecker
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UsePreciseSourceLocation()
        .Build();

    // Ending in any of these reads as a sentence/clause, not a heading label.
    private static readonly char[] SentencePunctuation = { '.', ',', ';', ':', '!', '?' };

    public static IReadOnlyList<EmphasisHeading> Check(string? markdown)
    {
        var document = Markdown.Parse(markdown ?? string.Empty, Pipeline);

        var findings = new List<EmphasisHeading>();
        foreach (var paragraph in document.OfType<ParagraphBlock>())
        {
            if (paragraph.Inline is null) continue;
            if (SoleEmphasis(paragraph.Inline) is not { } emphasis) continue;

            var text = PlainTextOf(emphasis).Trim();
            if (text.Length == 0) continue;
            if (SentencePunctuation.Contains(text[^1])) continue;

            findings.Add(new EmphasisHeading(text, paragraph.Line + 1));
        }

        return findings.OrderBy(f => f.Line).ToList();
    }

    // The paragraph qualifies only when its inline content is exactly one emphasis run,
    // ignoring whitespace-only literals the parser may leave around it (e.g. a trailing
    // space). A soft/hard line break, trailing prose, or a second emphasis all disqualify
    // it — those are sentences, not labels.
    private static EmphasisInline? SoleEmphasis(ContainerInline container)
    {
        EmphasisInline? found = null;
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal when literal.Content.ToString().Trim().Length == 0:
                    continue;
                case EmphasisInline emphasis when found is null:
                    found = emphasis;
                    break;
                default:
                    return null;
            }
        }
        return found;
    }

    private static string PlainTextOf(Inline inline)
    {
        var sb = new StringBuilder();
        if (inline is ContainerInline container)
            foreach (var child in container)
                sb.Append(PlainTextOf(child));
        else if (inline is LiteralInline literal)
            sb.Append(literal.Content.ToString());
        else if (inline is CodeInline code)
            sb.Append(code.Content);
        return sb.ToString();
    }
}
