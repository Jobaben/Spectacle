using System.Collections.Generic;
using System.Linq;
using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Spectacle.Render;

/// <summary>One link in the document that does not resolve, with its 1-based line.</summary>
public sealed record BrokenLink(string Target, int Line, string Reason);

/// <summary>
/// Validates a document's internal links: anchor links (<c>#section</c>) must
/// resolve to a heading slug or an explicit element id, and a link must have a
/// non-empty target. External and relative links are left alone. Uses the same
/// auto-identifier / generic-attribute behaviour the preview renders with, so
/// the slugs checked here match the ids the viewer actually emits.
/// </summary>
public static class LinkChecker
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseAutoIdentifiers()
        .UseGenericAttributes()
        .UsePreciseSourceLocation()
        .Build();

    public static IReadOnlyList<BrokenLink> Check(string? markdown)
    {
        var document = Markdown.Parse(markdown ?? string.Empty, Pipeline);

        var ids = new HashSet<string>();
        foreach (var obj in document.Descendants())
        {
            var id = obj.TryGetAttributes()?.Id;
            if (!string.IsNullOrEmpty(id)) ids.Add(id);
        }

        var findings = new List<BrokenLink>();
        foreach (var link in document.Descendants<LinkInline>())
        {
            if (link.IsImage) continue;

            var url = link.Url ?? string.Empty;
            var line = link.Line + 1;

            if (url.Length == 0)
                findings.Add(new BrokenLink(url, line, "empty link target"));
            else if (url.StartsWith('#'))
            {
                var anchor = url[1..];
                if (anchor.Length == 0 || !ids.Contains(anchor))
                    findings.Add(new BrokenLink(url, line, "anchor has no matching heading or id"));
            }
        }

        return findings.OrderBy(f => f.Line).ToList();
    }
}
