using System.Collections.Generic;
using System.Text;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Spectacle.Render;

/// <summary>
/// Walks a parsed Markdig document and pulls every heading into a flat outline,
/// in document order. Runs after the pipeline has processed the document so the
/// AutoIdentifier extension has already populated each heading's <c>id</c>.
/// </summary>
internal static class OutlineExtractor
{
    public static IReadOnlyList<OutlineEntry> Extract(MarkdownDocument document)
    {
        var result = new List<OutlineEntry>();

        foreach (var heading in document.Descendants<HeadingBlock>())
        {
            var text = InlineText(heading.Inline);
            // A heading with no rendered text (e.g. "###") produces no slug and is
            // not a useful navigation target, so skip it.
            if (text.Length == 0) continue;

            var id = heading.GetAttributes().Id ?? string.Empty;
            result.Add(new OutlineEntry(heading.Level, text, id, heading.Line + 1));
        }

        return result;
    }

    /// <summary>
    /// Flattens a heading's inlines to plain text: emphasis/links contribute their
    /// literal text, inline code its content, and a soft/hard break a single space.
    /// </summary>
    private static string InlineText(ContainerInline? container)
    {
        if (container is null) return string.Empty;

        var sb = new StringBuilder();
        foreach (var inline in container.Descendants())
        {
            switch (inline)
            {
                case LiteralInline literal:
                    sb.Append(literal.Content.ToString());
                    break;
                case CodeInline code:
                    sb.Append(code.Content);
                    break;
                case LineBreakInline:
                    sb.Append(' ');
                    break;
            }
        }

        return sb.ToString().Trim();
    }
}
