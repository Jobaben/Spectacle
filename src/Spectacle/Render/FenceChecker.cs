using System.Collections.Generic;
using System.Linq;

namespace Spectacle.Render;

/// <summary>One fenced-code-block integrity issue, with its 1-based line.</summary>
public sealed record FenceIssue(int Line, string Rule, string Message);

/// <summary>
/// Validates fenced code blocks — the kind AI agents routinely emit malformed.
/// Two rules: <c>unclosed-fence</c> (a fence opened but never closed, which swallows
/// the rest of the document into one code block — a real rendering defect) and
/// <c>no-language</c> (a closed fence with no language/info string, which renders
/// without syntax highlighting — advisory). Per CommonMark, a closing fence repeats
/// the opener's delimiter character at least as many times with no info string; a run
/// of the *other* delimiter character inside a block is content, not a toggle — so this
/// uses a state machine rather than a parity count.
/// </summary>
public static class FenceChecker
{
    /// <summary>The only rule that represents a rendering defect (the one that gates --review).</summary>
    public const string UnclosedRule = "unclosed-fence";

    public static IReadOnlyList<FenceIssue> Check(string? markdown)
    {
        var lines = (markdown ?? string.Empty).Split('\n');
        var issues = new List<FenceIssue>();

        var inFence = false;
        var openChar = '\0';
        var openLen = 0;
        var openLine = 0;
        var openHasLanguage = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var fence = ReadFence(lines[i]);
            if (fence is null) continue;
            var (fchar, flen, info) = fence.Value;

            if (!inFence)
            {
                inFence = true;
                openChar = fchar;
                openLen = flen;
                openLine = i + 1;
                openHasLanguage = info.Length > 0;
            }
            else if (fchar == openChar && flen >= openLen && info.Length == 0)
            {
                // A valid closing fence: same delimiter, at least as long, no info string.
                if (!openHasLanguage)
                    issues.Add(new FenceIssue(openLine, "no-language",
                        "fenced code block has no language tag"));
                inFence = false;
            }
            // Otherwise it is a delimiter line that cannot close this fence (wrong
            // character, too short, or carries an info string) — treat it as content.
        }

        if (inFence)
            issues.Add(new FenceIssue(openLine, UnclosedRule, "code fence opened here is never closed"));

        return issues.OrderBy(f => f.Line).ToList();
    }

    /// <summary>
    /// If the line is a fence delimiter (a run of at least three identical <c>`</c> or
    /// <c>~</c> characters), returns its delimiter character, run length, and trimmed
    /// info string; otherwise null. Leading whitespace is ignored, matching the
    /// fence-toggle convention the other line-based checks use.
    /// </summary>
    private static (char Char, int Len, string Info)? ReadFence(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.Length < 3) return null;

        var c = trimmed[0];
        if (c != '`' && c != '~') return null;

        var len = 0;
        while (len < trimmed.Length && trimmed[len] == c) len++;
        if (len < 3) return null;

        return (c, len, trimmed[len..].Trim());
    }
}
