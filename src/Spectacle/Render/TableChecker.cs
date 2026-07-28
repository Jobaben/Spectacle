using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Spectacle.Render;

/// <summary>One malformed GFM table row, with its 1-based line.</summary>
public sealed record TableIssue(int Line, string Message);

/// <summary>
/// Checks GFM pipe tables for column consistency: every separator and body row
/// must have the same number of cells as the header. Line-based and fence-aware
/// (heuristic: escaped pipes <c>\|</c> are not counted; pipes inside inline code
/// are not specially handled).
/// </summary>
public static class TableChecker
{
    // A separator row: pipes, dashes, colons and spaces, with at least one dash.
    private static readonly Regex Separator =
        new(@"^\s*\|?\s*:?-+:?\s*(\|\s*:?-+:?\s*)*\|?\s*$", RegexOptions.Compiled);

    public static IReadOnlyList<TableIssue> Check(string? markdown)
    {
        var source = markdown ?? string.Empty;
        var lines = source.Split('\n');
        var issues = new List<TableIssue>();
        var inCodeFence = false;

        var i = 0;
        while (i < lines.Length)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~"))
            {
                inCodeFence = !inCodeFence;
                i++;
                continue;
            }

            // A table is a header row followed by a separator row.
            if (!inCodeFence
                && lines[i].Contains('|')
                && i + 1 < lines.Length
                && Separator.IsMatch(lines[i + 1])
                && lines[i + 1].Contains('-'))
            {
                var headerCols = CountCells(lines[i]);

                var sepCols = CountCells(lines[i + 1]);
                if (sepCols != headerCols)
                    issues.Add(new TableIssue(i + 2,
                        $"separator has {sepCols} columns but the header has {headerCols}"));

                var j = i + 2;
                while (j < lines.Length && lines[j].Trim().Length > 0 && lines[j].Contains('|'))
                {
                    var cols = CountCells(lines[j]);
                    if (cols != headerCols)
                        issues.Add(new TableIssue(j + 1,
                            $"row has {cols} cells but the header has {headerCols}"));
                    j++;
                }

                i = j;
                continue;
            }

            i++;
        }

        return issues;
    }

    private static int CountCells(string line)
    {
        // Neutralize escaped pipes so they do not split cells.
        var work = line.Replace("\\|", "").Trim();
        if (work.StartsWith('|')) work = work[1..];
        if (work.EndsWith('|')) work = work[..^1];
        return work.Split('|').Length;
    }
}
