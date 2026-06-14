using System;
using System.Collections.Generic;
using System.Linq;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Spectacle.Render;

/// <summary>One relative link or image whose target does not resolve, with its 1-based line.</summary>
public sealed record BrokenPath(string Target, int Line, string Reason);

/// <summary>
/// Validates that a spec's <em>relative</em> link and image targets resolve to a real
/// file (or directory) on disk — the gap <see cref="LinkChecker"/> deliberately leaves
/// alone. AI agents routinely reference files and images that were never created
/// (hallucinated paths); this catches them. External targets (any URI scheme, or a
/// protocol-relative <c>//host</c>), pure in-document anchors (<c>#section</c>), and
/// site-absolute paths (<c>/foo</c>, not resolvable without a site root) are left alone —
/// only document-relative references are resolved. Filesystem resolution is injected as a
/// predicate so the discovery and classification rules stay pure and testable.
/// </summary>
public static class LinkPathChecker
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UsePreciseSourceLocation()
        .Build();

    /// <param name="targetExists">
    /// Returns whether a cleaned, document-relative target (fragment and query stripped,
    /// percent-decoded) resolves to something on disk. The caller composes it against the
    /// spec's own directory.
    /// </param>
    public static IReadOnlyList<BrokenPath> Check(string? markdown, Func<string, bool> targetExists)
    {
        ArgumentNullException.ThrowIfNull(targetExists);

        var document = Markdown.Parse(markdown ?? string.Empty, Pipeline);
        var findings = new List<BrokenPath>();

        foreach (var link in document.Descendants<LinkInline>())
        {
            var url = link.Url ?? string.Empty;
            if (!IsRelativeFileReference(url)) continue;

            var target = CleanTarget(url);
            if (target.Length == 0) continue;          // pure fragment/query — not a file reference
            if (targetExists(target)) continue;

            var kind = link.IsImage ? "image" : "link";
            findings.Add(new BrokenPath(url, link.Line + 1, $"relative {kind} target not found on disk"));
        }

        return findings.OrderBy(f => f.Line).ToList();
    }

    private static bool IsRelativeFileReference(string url)
    {
        if (url.Length == 0) return false;             // empty — LinkChecker owns this
        if (url.StartsWith('#')) return false;          // in-document anchor
        if (url.StartsWith("//")) return false;         // protocol-relative (external)
        if (url.StartsWith('/')) return false;          // site-absolute — no site root to resolve against
        if (HasUriScheme(url)) return false;            // http:, https:, mailto:, data:, tel: …
        return true;
    }

    /// <summary>
    /// True when the text up to the first colon is a legal URI scheme (letter, then
    /// letters/digits/<c>+</c>/<c>-</c>/<c>.</c>). This treats <c>http://x</c> and
    /// <c>mailto:x</c> as external; a relative path whose first colon sits after a
    /// slash (e.g. <c>dir/a:b</c>) is not a scheme and stays a file reference.
    /// </summary>
    private static bool HasUriScheme(string url)
    {
        var colon = url.IndexOf(':');
        if (colon <= 0) return false;
        for (var i = 0; i < colon; i++)
        {
            var ch = url[i];
            if (i == 0)
            {
                if (!char.IsLetter(ch)) return false;
            }
            else if (!(char.IsLetterOrDigit(ch) || ch is '+' or '-' or '.'))
            {
                return false;
            }
        }
        return true;
    }

    private static string CleanTarget(string url)
    {
        var cut = url.IndexOfAny(['#', '?']);
        var path = cut >= 0 ? url[..cut] : url;
        return Uri.UnescapeDataString(path);
    }
}
