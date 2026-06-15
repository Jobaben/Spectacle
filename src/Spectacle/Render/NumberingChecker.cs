using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;

namespace Spectacle.Render;

/// <summary>One ordered-list numbering defect, with its 1-based line.</summary>
public sealed record NumberingIssue(string Rule, int Line, string Message);

/// <summary>
/// Validates the numbering of ordered lists — the broken step/requirement sequences an AI
/// agent emits when it drops, duplicates, or reorders an item (<c>1. 2. 2. 4.</c>). A
/// reviewer skims a numbered spec by its numbers; when they jump or repeat, the spec reads as
/// if a step is missing even when the prose is intact.
///
/// Following markdownlint's MD029 <c>one_or_ordered</c> spirit, a list is accepted when its
/// source markers are either <em>all the same</em> (the lazy <c>1. 1. 1.</c> style every
/// Markdown renderer numbers sequentially) or <em>strictly consecutive</em> from whatever the
/// first item starts at (<c>1. 2. 3.</c>, <c>0. 1. 2.</c>, <c>3. 4. 5.</c>). Anything else —
/// a gap, a duplicate, or an out-of-order marker — is one finding, anchored at the first item
/// that breaks the run. Both legitimate styles pass, so false positives stay low enough to gate.
///
/// Each ordered list (including a nested one) is judged on its own, since CommonMark treats a
/// number change as continuing the list rather than starting a new one — so the raw source
/// numbers are exactly what a reviewer sees and what this check reads.
/// </summary>
public static class NumberingChecker
{
    /// <summary>An ordered list whose markers neither all match nor run consecutively.</summary>
    public const string OutOfSequenceRule = "out-of-sequence";

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UsePreciseSourceLocation()
        .Build();

    // The leading ordered-list marker on an item's source line: optional indentation, one to
    // nine digits, then a '.' or ')' delimiter. GFM caps an ordered marker at nine digits.
    private static readonly Regex Marker = new(@"^\s*(\d{1,9})[.)]", RegexOptions.Compiled);

    public static IReadOnlyList<NumberingIssue> Check(string? markdown)
    {
        var content = markdown ?? string.Empty;
        var document = Markdown.Parse(content, Pipeline);
        var lines = content.Split('\n');

        var issues = new List<NumberingIssue>();
        foreach (var list in document.Descendants<ListBlock>().Where(l => l.IsOrdered))
        {
            // Read each item's marker straight from the source line — Markdig normalizes the
            // rendered numbering, but the source numbers are what a reviewer reads.
            var markers = new List<(int Number, int Line)>();
            foreach (var item in list.OfType<ListItemBlock>())
            {
                if (item.Line < 0 || item.Line >= lines.Length) continue;
                var match = Marker.Match(lines[item.Line]);
                if (match.Success && int.TryParse(match.Groups[1].Value, out var number))
                    markers.Add((number, item.Line + 1));
            }

            // A single-item list has no sequence to break.
            if (markers.Count < 2) continue;

            // The lazy all-same style (1. 1. 1.) renders sequentially everywhere — leave it.
            var start = markers[0].Number;
            if (markers.All(m => m.Number == start)) continue;

            // Otherwise it must run consecutively from its start; report the first divergence.
            for (var i = 1; i < markers.Count; i++)
            {
                var expected = start + i;
                if (markers[i].Number == expected) continue;

                issues.Add(new NumberingIssue(
                    OutOfSequenceRule, markers[i].Line,
                    $"ordered list item numbered {markers[i].Number} breaks the sequence (expected {expected})"));
                break;
            }
        }

        return issues.OrderBy(i => i.Line).ToList();
    }
}
