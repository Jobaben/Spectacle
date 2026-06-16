using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Spectacle.Render;

/// <summary>
/// Enumerates a Markdown document's prose lines for text-level checks: lines inside fenced
/// code blocks are skipped entirely and inline code spans are blanked out, so a pattern that
/// looks for prose syntax (a reference link, a footnote marker) never fires on a literal value
/// shown in code. Shared by the reference-integrity checks (<see cref="LinkRefChecker"/>,
/// <see cref="FootnoteChecker"/>), which match raw syntax that Markdig drops to plain literal
/// text when it fails to resolve — and so cannot be located on the parsed AST.
/// </summary>
internal static class MarkdownTextScanner
{
    // A code span, one or more backticks delimiting a literal. Blanked before scanning so a
    // reference- or footnote-looking token shown as a value (e.g. `[^1]`) is never flagged.
    private static readonly Regex InlineCode = new(@"`+[^`]*`+", RegexOptions.Compiled);

    /// <summary>
    /// Yields each prose line as a (1-based line number, text) pair. Fenced code blocks
    /// (<c>```</c> or <c>~~~</c>) are skipped, and inline code spans are replaced with spaces of
    /// equal length so the surrounding text keeps its original column positions.
    /// </summary>
    public static IEnumerable<(int Line, string Text)> ProseLines(string? content)
    {
        var lines = (content ?? string.Empty).Split('\n');
        var inFence = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~"))
            {
                inFence = !inFence;
                continue;
            }
            if (inFence) continue;

            yield return (i + 1, InlineCode.Replace(lines[i], m => new string(' ', m.Length)));
        }
    }
}
