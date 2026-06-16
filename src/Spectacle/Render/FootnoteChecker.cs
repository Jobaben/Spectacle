using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;

namespace Spectacle.Render;

/// <summary>A footnote reference (<c>[^id]</c>) with no matching definition, with its 1-based line.</summary>
public sealed record UndefinedFootnote(string Label, int Line);

/// <summary>
/// Footnote integrity: flags footnote references (<c>[^id]</c>) that have no matching
/// <c>[^id]: …</c> definition. As with an unresolved reference link, Markdig renders an
/// undefined footnote marker as the literal text <c>[^id]</c> rather than a footnote, so a reader
/// sees a stray bracketed token where a citation should be — a common artifact when an AI agent
/// cites a footnote it forgot to define, or deletes a definition without removing its references.
/// The unresolved marker is plain text on the AST, so this check scans the raw <c>[^id]</c>
/// syntax; the definition set is read from the parsed document, where Markdig stores each
/// footnote definition as a <c>^</c>-prefixed <see cref="LinkReferenceDefinition"/>.
///
/// A definition's own opening marker (<c>[^id]:</c>) is not a reference and is left alone, and
/// markers inside code are ignored. Exits non-zero when any undefined footnote reference is
/// found, so it can gate a pipeline.
/// </summary>
public static class FootnoteChecker
{
    /// <summary>A footnote reference whose label has no matching definition.</summary>
    public const string UndefinedRule = "undefined-footnote";

    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().UseFootnotes().Build();

    // [^id] — a footnote marker. The negative lookahead for ':' skips a definition's own opening
    // marker ([^id]: …), leaving only references. Labels carry no whitespace or brackets.
    private static readonly Regex FootnoteUsage = new(
        @"\[\^(?<label>[^\]\s]+)\](?!:)", RegexOptions.Compiled);

    public static IReadOnlyList<UndefinedFootnote> Check(string? markdown)
    {
        var content = markdown ?? string.Empty;
        var defined = DefinedLabels(content);

        var findings = new List<UndefinedFootnote>();
        foreach (var (line, text) in MarkdownTextScanner.ProseLines(content))
        {
            foreach (Match m in FootnoteUsage.Matches(text))
            {
                var label = Normalize(m.Groups["label"].Value);
                if (defined.Contains(label)) continue;

                findings.Add(new UndefinedFootnote(label, line));
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
            // Footnote definitions are stored as reference definitions with a '^'-prefixed label.
            if (def.Label is { Length: > 0 } label && label[0] == '^')
                labels.Add(Normalize(label[1..]));
        }
        return labels;
    }

    private static string Normalize(string label) => label.Trim().ToLowerInvariant();
}
