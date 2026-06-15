using System.Collections.Generic;
using System.Linq;
using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Spectacle.Render;

/// <summary>A link whose visible text says nothing about its destination, with its 1-based line.</summary>
public sealed record UninformativeLink(string Text, int Line, string Reason);

/// <summary>
/// Accessibility / spec-quality check: reports links whose visible text does not describe
/// where they go — the <c>[click here](…)</c> / <c>[link](…)</c> / <c>[read more](…)</c>
/// boilerplate AI agents reach for instead of naming the destination. Link text is what a
/// screen reader announces out of context (a user tabbing through links hears only the text),
/// and "here" / "this" tells a reader nothing — the same class of WCAG defect
/// <see cref="AltTextChecker"/> catches for images, which no other check looks at for links
/// (<see cref="LinkChecker"/> validates only that a link's <em>target</em> resolves, never its text).
///
/// Two rules:
/// <list type="bullet">
///   <item><c>non-descriptive</c> — the text is one of a tight, curated set of generic phrases
///     (<c>click here</c>, <c>here</c>, <c>link</c>, <c>more</c>, <c>read more</c>, …), matching
///     the defaults of markdownlint's MD059.</item>
///   <item><c>empty</c> — the text between <c>[</c> and <c>]</c> is empty or only whitespace, so
///     the link renders with nothing to click (distinct from <see cref="LinkChecker"/>'s
///     empty-<em>target</em> rule).</item>
/// </list>
///
/// The phrase list is deliberately conservative — only wording that is non-descriptive in
/// essentially every context — to keep the false-positive rate low, the same stance
/// <see cref="ProseChecker"/> takes. Images are skipped (their text is alt text,
/// <see cref="AltTextChecker"/>'s concern).
/// </summary>
public static class LinkTextChecker
{
    public const string NonDescriptiveRule = "non-descriptive";
    public const string EmptyRule = "empty";

    // Curated, case-insensitive set of link texts that name no destination. Kept tight: every
    // entry is generic in essentially any spec, so flagging it is safe. MD059's defaults
    // ("click here", "here", "link", "more") plus the close variants AI agents emit.
    private static readonly HashSet<string> NonDescriptive = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "click here", "click", "here", "this", "this link", "link", "read more",
        "learn more", "more", "see here", "this page", "read this", "go here",
    };

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UsePreciseSourceLocation()
        .Build();

    public static IReadOnlyList<UninformativeLink> Check(string? markdown)
    {
        var document = Markdown.Parse(markdown ?? string.Empty, Pipeline);

        var findings = new List<UninformativeLink>();
        foreach (var link in document.Descendants<LinkInline>())
        {
            if (link.IsImage) continue;

            var text = TextOf(link);
            var line = link.Line + 1;
            var normalized = Normalize(text);

            if (normalized.Length == 0)
                findings.Add(new UninformativeLink(text, line, "empty link text"));
            else if (NonDescriptive.Contains(normalized))
                findings.Add(new UninformativeLink(text, line, $"non-descriptive link text '{text.Trim()}'"));
        }

        return findings.OrderBy(f => f.Line).ToList();
    }

    /// <summary>Whether a finding is the gating <c>empty</c>/<c>non-descriptive</c> rule, for SARIF rule ids.</summary>
    public static string RuleOf(UninformativeLink link) =>
        link.Reason.StartsWith("empty", System.StringComparison.Ordinal) ? EmptyRule : NonDescriptiveRule;

    // The visible text is the link's inline content. Concatenate every text-bearing descendant
    // so formatted text counts too — plain text and emphasis carry LiteralInline children, while
    // a link wrapping only a code span ([`run`](…)) carries a CodeInline — mirroring how
    // AltTextChecker reads an image's alt text.
    private static string TextOf(LinkInline link)
    {
        var sb = new StringBuilder();
        foreach (var inline in link.Descendants())
        {
            switch (inline)
            {
                case LiteralInline literal: sb.Append(literal.Content.ToString()); break;
                case CodeInline code: sb.Append(code.Content); break;
            }
        }
        return sb.ToString();
    }

    // Trim, drop surrounding punctuation an agent might tack on ("here." / "(more)"), and collapse
    // internal whitespace so "click  here" matches "click here". Case folding is left to the set's
    // ordinal-ignore-case comparer.
    private static string Normalize(string text)
    {
        var trimmed = text.Trim().Trim('.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '"', '\'').Trim();
        return string.Join(' ', trimmed.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries));
    }
}
