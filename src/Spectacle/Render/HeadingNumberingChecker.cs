using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;

namespace Spectacle.Render;

/// <summary>One manually-numbered-heading sequence defect, with its 1-based line.</summary>
public sealed record HeadingNumberingIssue(string Rule, int Line, string Message);

/// <summary>
/// Validates the numbering of <em>manually numbered headings</em> — the broken section
/// sequences an AI agent emits when it drops, duplicates, or reorders a section
/// (<c>## 1. Goals</c>, <c>## 2. Design</c>, <c>## 4. Rollout</c> — where did 3 go?). It is the
/// heading analogue of <see cref="NumberingChecker"/>, which judges ordered <em>lists</em> only;
/// a reviewer skims a numbered spec by its section numbers exactly as they skim a numbered list,
/// so a gap or repeat reads as a missing section even when the prose is intact.
///
/// Only flat, single-integer prefixes participate — a heading whose text begins with an integer
/// then <c>.</c> or <c>)</c> then whitespace (<c>1. </c>, <c>2) </c>, <c>10. </c>). Dotted
/// hierarchical numbering (<c>1.2 Detail</c>) is deliberately ignored: detecting it reliably and
/// validating a full outline is a different, far more false-positive-prone problem, and a spec
/// that never numbers its headings is wholly unaffected (the same "enforced only when present"
/// stance the TOC and section-template checks take).
///
/// Numbered headings are grouped into runs by heading level, and a run is closed whenever a
/// <em>shallower</em> heading intervenes — so sub-section numbering that legitimately restarts
/// under each new parent (<c>### 1.</c>, <c>### 2.</c> under one <c>##</c>, then <c>### 1.</c>
/// again under the next) is never flagged. Following markdownlint's MD029 <c>one_or_ordered</c>
/// spirit, each run passes when its numbers are either <em>all the same</em> (the lazy
/// <c>1. 1. 1.</c> style) or <em>strictly consecutive</em> from whatever the first heading starts
/// at; anything else is one <c>out-of-sequence</c> finding, anchored at the first heading that
/// breaks the run. It exits non-zero when a run is out of sequence, so it can gate a pipeline.
/// </summary>
public static class HeadingNumberingChecker
{
    /// <summary>A run of same-level numbered headings that neither all match nor run consecutively.</summary>
    public const string OutOfSequenceRule = "out-of-sequence";

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UsePreciseSourceLocation()
        .Build();

    // A flat numeric heading prefix: optional leading '#'s and whitespace (ATX markers; a setext
    // heading has none), an integer of one to nine digits, a '.' or ')' delimiter, then required
    // whitespace or end-of-line. The trailing-whitespace requirement is what excludes dotted
    // numbering — "1.2 Detail" has a digit, not a space, after the first '.', so it never matches.
    private static readonly Regex FlatPrefix = new(@"^\s*#*\s*(\d{1,9})[.)](\s|$)", RegexOptions.Compiled);

    public static IReadOnlyList<HeadingNumberingIssue> Check(string? markdown)
    {
        var content = markdown ?? string.Empty;
        var document = Markdown.Parse(content, Pipeline);
        var lines = content.Split('\n');

        // The maximal same-level runs to validate, plus the run currently open at each level.
        // A run is the list of (number, 1-based line) for consecutive numbered headings at one
        // level not interrupted by a shallower heading.
        var completed = new List<List<(int Number, int Line)>>();
        var open = new Dictionary<int, List<(int Number, int Line)>>();

        foreach (var heading in document.Descendants<HeadingBlock>())
        {
            if (heading.Line < 0 || heading.Line >= lines.Length) continue;
            var level = heading.Level;

            // A heading closes every run deeper than its own level — those sub-sections have ended.
            foreach (var deeper in open.Keys.Where(k => k > level).ToList())
            {
                completed.Add(open[deeper]);
                open.Remove(deeper);
            }

            var match = FlatPrefix.Match(lines[heading.Line]);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var number))
            {
                if (!open.TryGetValue(level, out var run))
                    open[level] = run = new List<(int, int)>();
                run.Add((number, heading.Line + 1));
            }
            else if (open.TryGetValue(level, out var run))
            {
                // An unnumbered sibling at this level breaks the numbered run, so a stray prose
                // heading between numbered sections never bridges a gap into a false positive.
                completed.Add(run);
                open.Remove(level);
            }
        }

        completed.AddRange(open.Values);

        var issues = new List<HeadingNumberingIssue>();
        foreach (var run in completed)
        {
            var issue = Evaluate(run);
            if (issue is not null) issues.Add(issue);
        }

        return issues.OrderBy(i => i.Line).ToList();
    }

    // Applies the MD029 one-or-ordered rule to a single same-level run: a run of fewer than two
    // numbered headings has no sequence to break; the lazy all-same style passes; otherwise the
    // numbers must run consecutively from the first, and the first divergence is the finding.
    private static HeadingNumberingIssue? Evaluate(List<(int Number, int Line)> run)
    {
        if (run.Count < 2) return null;

        var start = run[0].Number;
        if (run.All(m => m.Number == start)) return null;

        for (var i = 1; i < run.Count; i++)
        {
            var expected = start + i;
            if (run[i].Number == expected) continue;

            return new HeadingNumberingIssue(
                OutOfSequenceRule, run[i].Line,
                $"numbered heading {run[i].Number} breaks the sequence (expected {expected})");
        }

        return null;
    }
}
