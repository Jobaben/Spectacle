using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;

namespace Spectacle.Render;

/// <summary>
/// A reference-style link (or image) whose label resolves to no definition, carrying the
/// matched <see cref="Reference"/> syntax as written, the normalized <see cref="Label"/> that
/// failed to resolve, and the 1-based <see cref="Line"/>.
/// </summary>
public sealed record UndefinedReference(string Reference, string Label, int Line);

/// <summary>
/// Reference-link integrity: flags full (<c>[text][label]</c>) and collapsed (<c>[text][]</c>)
/// reference links and images whose label has no matching <c>[label]: url</c> definition.
/// CommonMark renders such an unresolved reference as the <em>literal</em> bracketed text — so
/// <c>[the API docs][api]</c> with no <c>[api]:</c> definition ships to the reader as the broken
/// string <c>[the API docs][api]</c>. AI agents routinely emit this when they restructure a spec
/// and drop the definition (or never write it). Because the unresolved form is plain text, it
/// never appears as a link on the parsed AST, so the existing <see cref="LinkChecker"/> — which
/// validates resolved anchor (<c>#section</c>) targets — cannot see it; this check scans the raw
/// reference syntax instead.
///
/// Only the full and collapsed forms are flagged: an undefined <em>shortcut</em> reference
/// (<c>[label]</c>) is, by the CommonMark spec, indistinguishable from ordinary bracketed prose
/// and renders cleanly, so it is never a defect. Definitions themselves are read from the parsed
/// document (Markdig's <see cref="LinkReferenceDefinition"/>), which handles indentation, titles,
/// and multi-line targets correctly; footnote definitions (a <c>^</c>-prefixed label) belong to
/// <see cref="FootnoteChecker"/> and are excluded here. Exits non-zero when any undefined
/// reference is found, so it can gate a pipeline.
/// </summary>
public static class LinkRefChecker
{
    /// <summary>A reference-style link/image whose label has no matching definition.</summary>
    public const string UndefinedRule = "undefined-reference";

    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    // [text][label]  or the collapsed [text][] — the explicit label group may be empty.
    // Backslash escapes are honoured so an escaped bracket inside text/label is not a delimiter.
    private static readonly Regex ReferenceUsage = new(
        @"\[(?<text>(?:\\.|[^\[\]\\])*)\]\[(?<label>(?:\\.|[^\[\]\\])*)\]",
        RegexOptions.Compiled);

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    public static IReadOnlyList<UndefinedReference> Check(string? markdown)
    {
        var content = markdown ?? string.Empty;
        var defined = DefinedLabels(content);

        var findings = new List<UndefinedReference>();
        foreach (var (line, text) in MarkdownTextScanner.ProseLines(content))
        {
            foreach (Match m in ReferenceUsage.Matches(text))
            {
                // A collapsed reference [text][] uses the visible text as its label.
                var explicitLabel = m.Groups["label"].Value;
                var label = Normalize(explicitLabel.Length == 0 ? m.Groups["text"].Value : explicitLabel);
                if (label.Length == 0 || defined.Contains(label)) continue;

                findings.Add(new UndefinedReference(m.Value, label, line));
            }
        }
        return findings.OrderBy(f => f.Line).ToList();
    }

    private static HashSet<string> DefinedLabels(string content)
    {
        var document = Markdown.Parse(content, Pipeline);
        var labels = new HashSet<string>();
        foreach (var def in document.Descendants<LinkReferenceDefinition>())
        {
            // Footnote definitions are stored as reference definitions with a '^'-prefixed
            // label; they are FootnoteChecker's concern, not a link reference.
            if (def.Label is { } label && !label.StartsWith('^'))
                labels.Add(Normalize(label));
        }
        return labels;
    }

    // CommonMark label matching: trim, collapse internal whitespace to one space, case-fold.
    private static string Normalize(string label) =>
        Whitespace.Replace(label.Trim(), " ").ToLowerInvariant();
}
