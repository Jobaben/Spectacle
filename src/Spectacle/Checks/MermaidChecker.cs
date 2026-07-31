using System;
using System.Collections.Generic;
using System.Linq;
using Markdig;
using Markdig.Syntax;
using Spectacle.Render;

namespace Spectacle.Checks;

/// <summary>A defect in a Mermaid diagram, with its rule id and 1-based line.</summary>
public sealed record MermaidIssue(int Line, string Rule, string Message);

/// <summary>
/// Checks the Mermaid diagrams in a document. Spectacle draws them, so a diagram that cannot be
/// drawn is a hole in the rendered document rather than a style nit — and the three ways a
/// generated diagram fails are all visible without running mermaid:
///
/// <list type="bullet">
///   <item><c>empty-diagram</c> — a <c>```mermaid</c> fence with nothing in it. The fence around a
///     diagram a workflow meant to fill in and did not; it renders as blank space.</item>
///   <item><c>unknown-diagram-type</c> — a diagram opening with a keyword mermaid does not
///     register. A model that has read about a diagram type mermaid does not ship (or invents a
///     plausible one, or gets the capitalization of a real one wrong) produces exactly this, and
///     every one of them fails to draw.</item>
///   <item><c>missing-description</c> — no <c>accTitle</c> or <c>accDescr</c>, so the diagram
///     reaches a screen reader as an unnamed graphic. This is <see cref="AltTextChecker"/>'s defect
///     in the other notation: a picture carrying meaning that only sighted readers receive.</item>
/// </list>
///
/// What this deliberately does not do is validate diagram syntax. Mermaid's grammars are the only
/// authority on those, and reimplementing even one of them would trade real findings for false
/// ones. A diagram that opens correctly and then fails to parse is caught where the authority
/// lives: <c>preview-mermaid.js</c> shows mermaid's own parse error in place of the drawing.
/// </summary>
public static class MermaidChecker
{
    /// <summary>A <c>```mermaid</c> fence with no diagram in it.</summary>
    public const string EmptyRule = "empty-diagram";

    /// <summary>A diagram opening with a keyword mermaid does not recognize.</summary>
    public const string UnknownTypeRule = "unknown-diagram-type";

    /// <summary>A diagram with neither <c>accTitle</c> nor <c>accDescr</c>.</summary>
    public const string MissingDescriptionRule = "missing-description";

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseYamlFrontMatter()
        .UsePreciseSourceLocation()
        .Build();

    // Ordinal, not OrdinalIgnoreCase: mermaid's own detectors are case-sensitive, so `classdiagram`
    // is a diagram that draws nothing and belongs in the report.
    private static readonly HashSet<string> Keywords =
        new(MermaidDiagram.DiagramKeywords, StringComparer.Ordinal);

    public static IReadOnlyList<MermaidIssue> Check(string? markdown)
    {
        var document = Markdown.Parse(markdown ?? string.Empty, Pipeline);
        var issues = new List<MermaidIssue>();

        foreach (var fence in document.Descendants<FencedCodeBlock>())
        {
            if (!MermaidDiagram.IsDiagramFence(fence.Info)) continue;
            Inspect(fence, issues);
        }

        return issues.OrderBy(i => i.Line).ThenBy(i => i.Rule, StringComparer.Ordinal).ToList();
    }

    private static void Inspect(FencedCodeBlock fence, List<MermaidIssue> issues)
    {
        // The opening fence's own line, which is where a defect belonging to the diagram as a whole
        // (it is empty; it has no description) is reported.
        var fenceLine = fence.Line + 1;
        var lines = ContentLines(fence);

        var head = FirstMeaningful(lines);
        if (head is null)
        {
            issues.Add(new MermaidIssue(fenceLine, EmptyRule,
                "mermaid fence contains no diagram"));
            // An empty diagram has no type to check and nothing to describe; one finding says it.
            return;
        }

        var (headText, headLine) = head.Value;
        var keyword = KeywordOf(headText);
        if (!Keywords.Contains(keyword))
        {
            issues.Add(new MermaidIssue(headLine, UnknownTypeRule,
                $"unknown mermaid diagram type: '{keyword}'"));
        }

        if (!lines.Any(l => DeclaresAccessibility(l.Text)))
        {
            issues.Add(new MermaidIssue(fenceLine, MissingDescriptionRule,
                "diagram has no accTitle or accDescr, so it reaches a screen reader unnamed"));
        }
    }

    // The fence's content with the file line each came from, so a finding points at the real line of
    // the real file rather than at an offset into the diagram.
    private static List<(string Text, int Line)> ContentLines(FencedCodeBlock fence)
    {
        var result = new List<(string, int)>(fence.Lines.Count);
        var lines = fence.Lines.Lines;
        for (var i = 0; i < fence.Lines.Count; i++)
        {
            // Content opens on the line after the fence, and file lines are 1-based.
            result.Add((lines[i].Slice.ToString(), fence.Line + i + 2));
        }
        return result;
    }

    /// <summary>
    /// The first line that declares the diagram's type, skipping what mermaid itself skips: blank
    /// lines, <c>%%</c> comments, an <c>%%{init: …}%%</c> directive, and a <c>---</c> YAML header
    /// (mermaid's own front matter, which carries <c>title</c> and <c>config</c> and comes before
    /// the diagram keyword). Returns <c>null</c> when there is no such line.
    /// </summary>
    private static (string Text, int Line)? FirstMeaningful(List<(string Text, int Line)> lines)
    {
        var inFrontMatter = false;
        var seenAnyContent = false;

        foreach (var (text, line) in lines)
        {
            var trimmed = text.Trim();

            if (inFrontMatter)
            {
                if (trimmed == "---") inFrontMatter = false;
                continue;
            }

            if (trimmed.Length == 0) continue;

            // Mermaid's front matter is only front matter at the very top of the diagram.
            if (trimmed == "---" && !seenAnyContent)
            {
                inFrontMatter = true;
                seenAnyContent = true;
                continue;
            }

            seenAnyContent = true;
            if (trimmed.StartsWith("%%", StringComparison.Ordinal)) continue;

            return (trimmed, line);
        }

        return null;
    }

    /// <summary>
    /// The diagram-type keyword at the head of <paramref name="headLine"/>: its first token, with
    /// the trailing punctuation a diagram may open with removed (<c>graph TD;</c>, <c>gitGraph:</c>,
    /// <c>pie showData</c>).
    /// </summary>
    private static string KeywordOf(string headLine)
    {
        var token = headLine.Split(' ', '\t')[0];
        return token.TrimEnd(':', ';', '{');
    }

    // accTitle takes a value (`accTitle: Login flow`); accDescr takes either a value or a braced
    // block (`accDescr {` … `}`), so the delimiter can be a colon or a brace.
    private static bool DeclaresAccessibility(string text)
    {
        var trimmed = text.TrimStart();
        return Declares(trimmed, "accTitle") || Declares(trimmed, "accDescr");
    }

    private static bool Declares(string trimmed, string directive)
    {
        if (!trimmed.StartsWith(directive, StringComparison.OrdinalIgnoreCase)) return false;
        var rest = trimmed.AsSpan(directive.Length).TrimStart();
        return rest.Length > 0 && (rest[0] == ':' || rest[0] == '{');
    }
}
