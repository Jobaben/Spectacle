using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;

namespace Spectacle.Render;

/// <summary>One readiness issue found in a spec, with a 1-based line number.</summary>
public sealed record SpecLintFinding(string Rule, int Line, string Message);

/// <summary>
/// Lightweight readiness checks for AI-authored specs — the kind of gaps an
/// agent commonly leaves behind. Two rules: <c>placeholder</c> (leftover
/// markers such as TODO/TBD/FIXME/&lt;placeholder&gt;/lorem ipsum, ignoring
/// fenced code) and <c>empty-section</c> (a heading with no content of its own
/// and no subsection beneath it). Pure text/AST analysis — no rendering.
/// </summary>
public static class SpecLinter
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    private static readonly (string Label, Regex Pattern)[] Markers =
    {
        ("TODO", new Regex(@"\bTODO\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("TBD", new Regex(@"\bTBD\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("FIXME", new Regex(@"\bFIXME\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("XXX", new Regex(@"\bXXX\b", RegexOptions.Compiled)),
        ("lorem ipsum", new Regex(@"lorem ipsum", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("<placeholder>", new Regex(@"<placeholder>", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("[INSERT", new Regex(@"\[INSERT", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
    };

    public static IReadOnlyList<SpecLintFinding> Lint(string? markdown)
    {
        var source = markdown ?? string.Empty;
        var findings = new List<SpecLintFinding>();
        findings.AddRange(ScanPlaceholders(source));
        findings.AddRange(ScanEmptySections(source));
        return findings.OrderBy(f => f.Line).ToList();
    }

    private static IEnumerable<SpecLintFinding> ScanPlaceholders(string source)
    {
        var lines = source.Split('\n');
        var inCodeFence = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~"))
            {
                inCodeFence = !inCodeFence;
                continue;
            }
            if (inCodeFence) continue;

            foreach (var (label, pattern) in Markers)
            {
                if (pattern.IsMatch(lines[i]))
                    yield return new SpecLintFinding("placeholder", i + 1, $"placeholder marker '{label}'");
            }
        }
    }

    private static IEnumerable<SpecLintFinding> ScanEmptySections(string source)
    {
        // Markdig always appends a (often empty) LinkReferenceDefinitionGroup as
        // the last block; it is not visible section content, so drop it before
        // deciding whether a trailing heading is empty.
        var blocks = Markdown.Parse(source, Pipeline)
            .Where(b => b is not LinkReferenceDefinitionGroup)
            .ToList();

        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is not HeadingBlock heading) continue;

            // Empty when the next block is a sibling/ancestor heading (level <=
            // this one) or there is no following block at all. A deeper heading
            // next means this section has a subsection, so it is not empty.
            var next = i + 1 < blocks.Count ? blocks[i + 1] : null;
            var isEmpty = next is null
                || (next is HeadingBlock nextHeading && nextHeading.Level <= heading.Level);

            if (isEmpty)
                yield return new SpecLintFinding("empty-section", heading.Line + 1, "heading has no content");
        }
    }
}
