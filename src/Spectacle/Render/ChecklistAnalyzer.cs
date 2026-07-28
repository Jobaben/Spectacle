using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Spectacle.Render;

/// <summary>One GFM task-list item, with its 1-based source line.</summary>
public sealed record ChecklistItem(bool Checked, string Text, int Line);

/// <summary>
/// Extracts GFM task-list items (<c>- [ ]</c> / <c>- [x]</c>) from a spec so a
/// reviewer can see acceptance-criteria completion at a glance. Line-based and
/// fence-aware (task syntax inside fenced code is ignored).
/// </summary>
public static class ChecklistAnalyzer
{
    // Bullet (-, *, +) then a [ ], [x] or [X] checkbox then the item text.
    private static readonly Regex TaskLine =
        new(@"^\s*[-*+]\s+\[([ xX])\]\s*(.*)$", RegexOptions.Compiled);

    public static IReadOnlyList<ChecklistItem> Analyze(string? markdown)
    {
        var source = markdown ?? string.Empty;
        var items = new List<ChecklistItem>();
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

            var m = TaskLine.Match(lines[i]);
            if (!m.Success) continue;

            var isChecked = m.Groups[1].Value is "x" or "X";
            items.Add(new ChecklistItem(isChecked, m.Groups[2].Value.Trim(), i + 1));
        }

        return items;
    }
}
