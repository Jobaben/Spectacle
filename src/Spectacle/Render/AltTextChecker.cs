using System.Collections.Generic;
using System.Linq;
using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Spectacle.Render;

/// <summary>An image whose alt text is empty or whitespace, with its 1-based line.</summary>
public sealed record ImageWithoutAlt(string Target, int Line);

/// <summary>
/// Accessibility check: reports images written with no alt text — the
/// <c>![](path.png)</c> form an AI agent emits when it drops a screenshot or diagram
/// into a spec without describing it. Alt text is what a screen reader announces and
/// what survives when the image fails to load, so a missing description is a real
/// WCAG defect, not a style nit. None of the other checks look at images:
/// <see cref="LinkChecker"/> deliberately skips them.
///
/// An image is flagged when the text between <c>![</c> and <c>]</c> is empty or only
/// whitespace. The target (URL) is reported so the finding points at a recognizable
/// image; relative-target existence is the separate concern of <see cref="LinkPathChecker"/>.
/// </summary>
public static class AltTextChecker
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UsePreciseSourceLocation()
        .Build();

    public static IReadOnlyList<ImageWithoutAlt> Check(string? markdown)
    {
        var document = Markdown.Parse(markdown ?? string.Empty, Pipeline);

        var findings = new List<ImageWithoutAlt>();
        foreach (var image in document.Descendants<LinkInline>())
        {
            if (!image.IsImage) continue;
            if (string.IsNullOrWhiteSpace(AltTextOf(image)))
                findings.Add(new ImageWithoutAlt(image.Url ?? string.Empty, image.Line + 1));
        }

        return findings.OrderBy(f => f.Line).ToList();
    }

    // The alt text is the image link's inline content (e.g. the literal in ![alt](url)).
    // Concatenate every text-bearing descendant so formatted alt text counts: plain text
    // and emphasis (**bold**, _italic_) carry LiteralInline children, while an alt that is
    // only an inline code span (![`config.svg`](…)) is a CodeInline, which is not a
    // LiteralInline — counting both avoids falsely flagging an image that does have alt text.
    private static string AltTextOf(LinkInline image)
    {
        var sb = new StringBuilder();
        foreach (var inline in image.Descendants())
        {
            switch (inline)
            {
                case LiteralInline literal: sb.Append(literal.Content.ToString()); break;
                case CodeInline code: sb.Append(code.Content); break;
            }
        }
        return sb.ToString();
    }
}
