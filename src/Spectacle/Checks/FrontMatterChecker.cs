using System;
using System.Collections.Generic;
using System.Linq;
using Spectacle.Gate;

namespace Spectacle.Checks;

/// <summary>One front-matter defect, with its rule id and 1-based line.</summary>
public sealed record FrontMatterFinding(string Rule, int Line, string Message);

/// <summary>
/// Validates the YAML metadata header an AI workflow stamps onto the Markdown it writes.
///
/// This is the check that makes a gate *addressable by a workflow*. The prose checks ask "is this
/// document well formed?"; this one asks "does this document declare who produced it, from which
/// prompt, at which stage?" — the fields a pipeline needs in order to route, attribute, or
/// re-dispatch the file. Declare the template once as <c>requiredFrontMatter</c> in
/// <c>.spectacle.json</c> and every generated document is held to it.
///
/// Five rules:
/// <list type="bullet">
///   <item><c>missing-front-matter</c> — a template is declared but the document has no header.</item>
///   <item><c>unclosed-front-matter</c> — the header opens with <c>---</c> and never closes, so no
///     parser reads it as metadata and the whole document renders as one broken block. A truncated
///     generator response looks exactly like this.</item>
///   <item><c>missing-key</c> — a required key the header never declares.</item>
///   <item><c>empty-value</c> — a required key present but blank: the template was copied and
///     never filled in, which is worse than absent because it looks complete.</item>
///   <item><c>duplicate-key</c> — the same key twice; YAML keeps the last, so the value a reader
///     sees and the value a parser returns can differ.</item>
///   <item><c>misplaced-front-matter</c> — a second metadata header further down the document,
///     the signature of concatenated generator output. It is not front matter to any parser: it
///     renders as a stray heading and a horizontal rule.</item>
/// </list>
///
/// Every rule is silent on a document with no header and no declared template, so a project that
/// does not use front matter is completely unaffected.
/// </summary>
public static class FrontMatterChecker
{
    public const string MissingHeaderRule = "missing-front-matter";
    public const string UnclosedRule = "unclosed-front-matter";
    public const string MissingKeyRule = "missing-key";
    public const string EmptyValueRule = "empty-value";
    public const string DuplicateKeyRule = "duplicate-key";
    public const string MisplacedRule = "misplaced-front-matter";

    /// <summary>
    /// Checks <paramref name="content"/>'s header against <paramref name="requiredKeys"/> — the
    /// project's metadata template, empty to require nothing. Keys are matched
    /// case-insensitively and may name a nested field as a dotted path (<c>run.model</c>).
    /// </summary>
    public static IReadOnlyList<FrontMatterFinding> Check(string? content, IReadOnlyList<string> requiredKeys)
    {
        var required = Normalize(requiredKeys);
        var header = FrontMatter.Parse(content);
        var findings = new List<FrontMatterFinding>();

        if (!header.Present)
        {
            if (required.Count != 0)
                findings.Add(new FrontMatterFinding(
                    MissingHeaderRule, 1,
                    $"no front matter; the project requires {Describe(required)}"));
        }
        else if (!header.Closed)
        {
            // The header is unusable, so key-level rules would only add noise on top of the one
            // defect that matters: there is no closing fence.
            findings.Add(new FrontMatterFinding(
                UnclosedRule, header.StartLine,
                "front matter opened with '---' but never closed; no parser will read it as metadata"));
        }
        else
        {
            findings.AddRange(DuplicateKeys(header));
            findings.AddRange(RequiredKeys(header, required));
        }

        findings.AddRange(MisplacedHeaders(content, header));
        return findings.OrderBy(f => f.Line).ToList();
    }

    /// <summary>Parses the required-key template from a comma-separated CLI list.</summary>
    public static IReadOnlyList<string> ParseRequired(string? list) =>
        (list ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

    private static IReadOnlyList<string> Normalize(IReadOnlyList<string> keys) =>
        keys.Select(k => k.Trim()).Where(k => k.Length != 0).ToList();

    private static IEnumerable<FrontMatterFinding> DuplicateKeys(FrontMatterBlock header)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in header.Entries)
        {
            if (!seen.Add(entry.Key))
                yield return new FrontMatterFinding(
                    DuplicateKeyRule, entry.Line,
                    $"duplicate front-matter key '{entry.Key}'; only the last value survives parsing");
        }
    }

    private static IEnumerable<FrontMatterFinding> RequiredKeys(
        FrontMatterBlock header, IReadOnlyList<string> required)
    {
        foreach (var key in required)
        {
            var entry = header.Find(key);
            if (entry is null)
                yield return new FrontMatterFinding(
                    MissingKeyRule, header.StartLine,
                    $"front matter is missing required key '{key}'");
            else if (!entry.HasValue)
                yield return new FrontMatterFinding(
                    EmptyValueRule, entry.Line,
                    $"required front-matter key '{key}' is present but empty");
        }
    }

    // A metadata header below the document's own front matter. Deliberately strict — it must be
    // a `---` fence, then at least two consecutive `key: value` lines, then a closing fence —
    // because a single `key: value` line between two horizontal rules is something a human might
    // legitimately write, whereas a two-key block is the shape of a generator's header.
    // Fenced code is skipped: a YAML sample in a code block is documentation, not a defect.
    private static IEnumerable<FrontMatterFinding> MisplacedHeaders(string? content, FrontMatterBlock header)
    {
        var lines = (content ?? string.Empty).Split('\n');
        // Start below the document's own header (if any) so it is never reported as misplaced.
        var start = header.Present && header.Closed ? header.EndLine : 1;
        var inFence = false;

        for (var i = start; i < lines.Length; i++)
        {
            var text = lines[i].Trim();
            if (text.StartsWith("```", StringComparison.Ordinal) || text.StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }
            if (inFence || text != "---") continue;

            var keys = 0;
            var j = i + 1;
            for (; j < lines.Length; j++)
            {
                var candidate = lines[j].Trim();
                if (candidate.Length == 0) break;
                if (candidate == "---" || candidate == "...") break;
                if (!LooksLikeKeyLine(candidate)) break;
                keys++;
            }

            var closed = j < lines.Length && (lines[j].Trim() == "---" || lines[j].Trim() == "...");
            if (keys >= 2 && closed)
            {
                // The line is carried by the finding, not repeated in the message: the baseline
                // delta keys a finding's identity on its message, so a line number inside it
                // would make an unmoved defect read as one fixed plus one new after any edit above.
                yield return new FrontMatterFinding(
                    MisplacedRule, i + 1,
                    "front-matter block below the top of the document, so it renders as prose instead of metadata");
                i = j; // Don't re-report the same block from its closing fence.
            }
        }
    }

    private static bool LooksLikeKeyLine(string text)
    {
        var colon = text.IndexOf(':');
        if (colon <= 0 || colon == text.Length - 1) return false;
        if (text[colon + 1] != ' ') return false;

        var key = text[..colon];
        return key.All(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.');
    }

    private static string Describe(IReadOnlyList<string> required) =>
        required.Count == 1 ? $"the key '{required[0]}'" : "the keys " + string.Join(", ", required.Select(k => $"'{k}'"));
}
