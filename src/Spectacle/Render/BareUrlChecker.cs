using System.Collections.Generic;
using System.Linq;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Spectacle.Render;

/// <summary>A bare (auto-linked) URL in prose that should be a descriptive link, with its 1-based line.</summary>
public sealed record BareUrl(string Url, int Line);

/// <summary>
/// Accessibility / spec-quality check: reports bare URLs pasted straight into the prose —
/// <c>https://example.com</c> sitting in a sentence rather than a descriptive Markdown link.
/// GFM auto-links such text, so it renders as a link whose visible text is the raw URL: a
/// screen reader reads the whole address aloud and a reader scanning the page learns nothing
/// about where it goes. It is the link analogue of the <see cref="AltTextChecker"/> defect and
/// the worst case of the non-descriptive text <see cref="LinkTextChecker"/> catches — the text
/// <em>is</em> the URL — which is why neither of those checks looks at it (a bare URL has no
/// authored text to inspect).
///
/// Only the bare, undelimited form is flagged. The two legitimate ways to write a URL verbatim
/// are left alone, so the rule keeps a clean escape hatch:
/// <list type="bullet">
///   <item>an explicit autolink — <c>&lt;https://example.com&gt;</c> — the CommonMark syntax for
///     "render this URL as a link on purpose" (a <c>Markdig.Syntax.Inlines.AutolinkInline</c>,
///     not the <see cref="LinkInline"/> an auto-linked bare URL produces);</item>
///   <item>a code span — <c>`https://example.com`</c> — when the URL is a literal value (an API
///     endpoint, an example), not a link; Markdig never auto-links inside code, so it never fires.</item>
/// </list>
/// A proper <c>[text](url)</c> link is an ordinary <see cref="LinkInline"/> (not an autolink) and
/// is never flagged. URLs inside fenced or indented code are skipped for the same reason a code
/// span is. It exits non-zero when any bare URL is found, so it can gate a pipeline.
/// </summary>
public static class BareUrlChecker
{
    /// <summary>A bare URL auto-linked from undelimited prose text.</summary>
    public const string BareUrlRule = "bare-url";

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UsePreciseSourceLocation()
        .Build();

    public static IReadOnlyList<BareUrl> Check(string? markdown)
    {
        var document = Markdown.Parse(markdown ?? string.Empty, Pipeline);

        var findings = new List<BareUrl>();
        foreach (var link in document.Descendants<LinkInline>())
        {
            // Only an auto-linked bare URL — never an explicit <url> autolink (a separate
            // AutolinkInline) nor a proper [text](url) link (IsAutoLink is false). Images carry
            // alt text, which is AltTextChecker's concern, so they are skipped.
            if (!link.IsAutoLink || link.IsImage) continue;

            findings.Add(new BareUrl(DisplayText(link), link.Line + 1));
        }

        return findings.OrderBy(f => f.Line).ToList();
    }

    // The text the reader sees is the URL exactly as written in the source — the autolink's
    // single literal child. Markdig normalizes a scheme-less www. URL's Url property to add a
    // protocol (www.x → http://www.x); reporting the literal keeps the finding pointing at what
    // the author actually typed. Fall back to the resolved Url if no literal is present.
    private static string DisplayText(LinkInline link)
    {
        if (link.FirstChild is LiteralInline literal)
        {
            var text = literal.Content.ToString();
            if (text.Length != 0) return text;
        }
        return link.Url ?? string.Empty;
    }
}
