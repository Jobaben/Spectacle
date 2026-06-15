using System;
using System.Collections.Generic;
using System.Linq;

namespace Spectacle.Render;

/// <summary>
/// Per-line review suppressions an author embeds in the spec itself, so a deliberate finding
/// is marked once instead of failing the gate forever. This is the line-level companion to the
/// project-level <see cref="ReviewChecks"/> gate: where that turns a check off everywhere, this
/// silences one finding at one place — the same <c>eslint-disable-next-line</c> / <c>noqa</c>
/// mechanism every linter ships, here serving the AI write→review→revise loop (an agent that
/// repeated a paragraph on purpose annotates it rather than fighting the gate).
///
/// Two directives, written as HTML comments (invisible in the rendered preview):
/// <list type="bullet">
///   <item><c>&lt;!-- spectacle-disable-line [ids] --&gt;</c> — suppress on the same line.</item>
///   <item><c>&lt;!-- spectacle-disable-next-line [ids] --&gt;</c> — suppress on the next line.</item>
/// </list>
/// <c>[ids]</c> is an optional comma/space-separated list of check ids
/// (<c>duplication</c>, <c>alt-text</c>, …); omit it to suppress every check on that line.
/// Directives inside fenced code are ignored — a code sample documenting the syntax is not a
/// live directive — mirroring how the content checks skip fenced code.
/// </summary>
public sealed class InlineSuppressions
{
    private const string DisableLine = "spectacle-disable-line";
    private const string DisableNextLine = "spectacle-disable-next-line";
    private const string AllChecks = "*";

    // 1-based line number -> the check ids suppressed there ("*" = all checks).
    private readonly Dictionary<int, HashSet<string>> _byLine;

    private InlineSuppressions(Dictionary<int, HashSet<string>> byLine) => _byLine = byLine;

    public static readonly InlineSuppressions None = new(new Dictionary<int, HashSet<string>>());

    /// <summary>True when no directive was found, so callers can skip per-finding filtering.</summary>
    public bool IsEmpty => _byLine.Count == 0;

    /// <summary>Whether a <paramref name="checkId"/> finding on 1-based <paramref name="line"/> is suppressed.</summary>
    public bool IsSuppressed(string checkId, int line) =>
        _byLine.TryGetValue(line, out var ids) && (ids.Contains(AllChecks) || ids.Contains(checkId));

    /// <summary>Scans the spec for suppression directives, skipping fenced code blocks.</summary>
    public static InlineSuppressions Parse(string? content)
    {
        var byLine = new Dictionary<int, HashSet<string>>();
        var lines = (content ?? string.Empty).Split('\n');
        var inCodeFence = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                inCodeFence = !inCodeFence;
                continue;
            }
            if (inCodeFence) continue;

            // The line is 1-based; next-line targets the following 1-based line.
            var lineNo = i + 1;
            if (TryParseDirective(lines[i], DisableNextLine, out var nextIds))
                Add(byLine, lineNo + 1, nextIds);
            else if (TryParseDirective(lines[i], DisableLine, out var sameIds))
                Add(byLine, lineNo, sameIds);
        }

        return byLine.Count == 0 ? None : new InlineSuppressions(byLine);
    }

    // Recognizes `<!-- <keyword> [ids] -->` anywhere on the line. next-line is checked first by
    // the caller because its keyword contains the disable-line keyword as a suffix-free prefix.
    private static bool TryParseDirective(string line, string keyword, out HashSet<string> ids)
    {
        ids = new HashSet<string>(StringComparer.Ordinal);

        var open = line.IndexOf("<!--", StringComparison.Ordinal);
        if (open < 0) return false;
        var close = line.IndexOf("-->", open, StringComparison.Ordinal);
        if (close < 0) return false;

        var inner = line[(open + 4)..close].Trim();
        if (!inner.StartsWith(keyword, StringComparison.Ordinal)) return false;

        // What follows the keyword must be a word boundary, so `spectacle-disable-line` does not
        // match `spectacle-disable-line-foo` (and disable-next-line is matched before disable-line).
        var rest = inner[keyword.Length..];
        if (rest.Length > 0 && rest[0] is not (' ' or '\t' or ',')) return false;

        var tokens = rest.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.ToLowerInvariant());
        foreach (var t in tokens) ids.Add(t);

        // No explicit ids means "every check on this line".
        if (ids.Count == 0) ids.Add(AllChecks);
        return true;
    }

    private static void Add(Dictionary<int, HashSet<string>> byLine, int line, HashSet<string> ids)
    {
        if (byLine.TryGetValue(line, out var existing)) existing.UnionWith(ids);
        else byLine[line] = ids;
    }
}
