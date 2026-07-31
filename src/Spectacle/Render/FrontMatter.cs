using System;
using System.Collections.Generic;
using System.Linq;
using Spectacle.Checks;

namespace Spectacle.Render;

/// <summary>
/// One front-matter key with its resolved value and 1-based source line. A key holds either a
/// scalar (<see cref="Value"/>), a sequence (<see cref="Items"/>), or child keys
/// (<see cref="IsMapping"/>) — the three YAML shapes an AI workflow's metadata header uses.
/// Nested keys are also surfaced flattened, as dotted paths (<c>run.model</c>), so a required-key
/// template can name a nested field without the config learning YAML.
/// </summary>
public sealed record FrontMatterEntry(
    string Key, string Value, IReadOnlyList<string> Items, int Line, bool IsMapping = false)
{
    /// <summary>Whether this key carries a sequence of values.</summary>
    public bool IsList => Items.Count != 0;

    /// <summary>
    /// Whether the key was actually filled in. A key present but blank (<c>status:</c> with
    /// nothing after it) is the signature of a template header a generator never populated,
    /// so it counts as *absent* for gating purposes.
    /// </summary>
    public bool HasValue => Value.Trim().Length != 0 || Items.Count != 0 || IsMapping;
}

/// <summary>
/// The YAML front-matter header of a Markdown document, parsed into ordered key entries.
///
/// Front matter is how an AI workflow stamps provenance onto the Markdown it writes — which
/// agent wrote it, against which prompt, at which gate stage — so Spectacle treats it as data
/// rather than prose: it is rendered as a metadata card, validated against a required-key
/// template, and echoed into the gate verdict so a downstream step can route on it.
///
/// The parser is a deliberate YAML *subset* — scalars, quoted scalars, block and flow sequences,
/// and nested mappings by indentation. That covers every metadata header a generator emits
/// without taking on a YAML dependency, and an unrecognized construct is skipped rather than
/// throwing: a malformed header must never crash a headless gate.
/// </summary>
public sealed record FrontMatterBlock(
    bool Present,
    bool Closed,
    int StartLine,
    int EndLine,
    IReadOnlyList<FrontMatterEntry> Entries)
{
    /// <summary>A document with no front-matter header.</summary>
    public static readonly FrontMatterBlock Absent =
        new(false, false, 0, 0, Array.Empty<FrontMatterEntry>());

    /// <summary>
    /// The entry for <paramref name="key"/> (case-insensitive, dotted path for a nested key),
    /// or <c>null</c> when the header does not declare it.
    /// </summary>
    public FrontMatterEntry? Find(string key) =>
        Entries.FirstOrDefault(e => string.Equals(e.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The header as flat display pairs, in source order, for a metadata card or a verdict
    /// echo. A sequence joins on ", "; a mapping parent is dropped (its children carry the
    /// values), so the pairs are exactly the leaves a reader wants to see.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> Metadata =>
        Entries.Where(e => !e.IsMapping)
            .Select(e => new KeyValuePair<string, string>(
                e.Key, e.IsList ? string.Join(", ", e.Items) : e.Value))
            .ToList();
}

/// <summary>
/// Parses and strips a Markdown document's YAML front matter.
/// </summary>
public static class FrontMatter
{
    /// <summary>
    /// Parses the front-matter header. A header is recognized only when the document's very
    /// first line is the <c>---</c> fence — the same rule every static-site generator and
    /// Markdown parser applies, so what Spectacle calls front matter is what the rest of the
    /// toolchain calls front matter. A <c>---</c> block further down the document is ordinary
    /// Markdown (and is what <see cref="FrontMatterChecker"/>'s misplaced-header rule reports).
    /// </summary>
    public static FrontMatterBlock Parse(string? content)
    {
        var lines = SplitLines(content);
        if (lines.Length == 0) return FrontMatterBlock.Absent;

        // A BOM sits in front of the opening fence in a UTF-8-with-signature file.
        if (!IsOpeningFence(lines[0].TrimStart('\uFEFF'))) return FrontMatterBlock.Absent;

        // The closing fence is `---` (YAML document separator) or `...` (document end); both
        // are in use by generators, so both close the header.
        var close = -1;
        for (var i = 1; i < lines.Length; i++)
        {
            if (IsClosingFence(lines[i])) { close = i; break; }
        }

        var end = close < 0 ? lines.Length - 1 : close;
        return new FrontMatterBlock(
            Present: true,
            Closed: close >= 0,
            StartLine: 1,
            EndLine: end + 1,
            Entries: ParseEntries(lines, 1, close < 0 ? lines.Length : close));
    }

    /// <summary>
    /// Returns <paramref name="content"/> with the front-matter header replaced by blank lines,
    /// so every prose check sees the document body while its findings keep pointing at the
    /// original line numbers.
    ///
    /// This is what keeps a metadata header from being read as prose. Without it a
    /// <c>title: Draft</c> line followed by the closing <c>---</c> is a setext heading to any
    /// CommonMark parser, so the header silently becomes the document's first h2 — polluting the
    /// heading hierarchy, the outline, and the table-of-contents check on essentially every
    /// document an AI workflow produces.
    ///
    /// An <em>unclosed</em> header is left untouched: there is no fence to bound, so blanking
    /// would swallow the whole document and hide every other finding. The unclosed header is
    /// reported by its own rule instead.
    /// </summary>
    public static string Strip(string? content)
    {
        var block = Parse(content);
        if (!block.Present || !block.Closed) return content ?? string.Empty;

        var lines = SplitLines(content);
        for (var i = 0; i < block.EndLine && i < lines.Length; i++) lines[i] = string.Empty;
        return string.Join("\n", lines);
    }

    /// <summary>
    /// The document body with its front matter stripped, paired with the parsed header — the
    /// one call a check pipeline needs to see prose as prose and metadata as metadata.
    /// </summary>
    public static (FrontMatterBlock Header, string Body) Split(string? content) =>
        (Parse(content), Strip(content));

    // Splits on '\n' without normalizing: a preserved line keeps its trailing '\r', so
    // rejoining on '\n' reproduces the original CRLF document byte for byte.
    private static string[] SplitLines(string? content) =>
        (content ?? string.Empty).Split('\n');

    private static bool IsOpeningFence(string line) => line.TrimEnd() == "---";

    private static bool IsClosingFence(string line)
    {
        var t = line.TrimEnd();
        return t == "---" || t == "...";
    }

    private static IReadOnlyList<FrontMatterEntry> ParseEntries(string[] lines, int from, int toExclusive)
    {
        var entries = new List<FrontMatterEntry>();
        // Open parent keys by indentation, innermost last, so a nested key resolves to a dotted path.
        var parents = new List<(int Indent, string Path)>();

        for (var i = from; i < toExclusive && i < lines.Length; i++)
        {
            var raw = lines[i].TrimEnd('\r');
            var trimmed = raw.TrimStart();
            if (trimmed.Length == 0 || trimmed[0] == '#') continue;

            var indent = raw.Length - trimmed.Length;

            // A block-sequence item belongs to the most recently declared key.
            if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed == "-")
            {
                var item = Unquote(trimmed.Length > 1 ? trimmed[1..].Trim() : string.Empty);
                if (item.Length != 0 && entries.Count != 0)
                {
                    var last = entries[^1];
                    entries[^1] = last with { Items = last.Items.Append(item).ToList() };
                }
                continue;
            }

            var separator = KeySeparator(trimmed);
            if (separator < 0) continue;

            var name = trimmed[..separator].Trim();
            if (name.Length == 0 || !IsPlausibleKey(name)) continue;

            while (parents.Count != 0 && parents[^1].Indent >= indent) parents.RemoveAt(parents.Count - 1);
            var path = parents.Count == 0 ? name : parents[^1].Path + "." + name;

            var rest = trimmed[(separator + 1)..].Trim();
            if (rest.Length == 0)
            {
                // Either a mapping parent or a block-sequence owner — which one only the
                // following lines reveal, so open it and let the post-pass decide.
                parents.Add((indent, path));
                entries.Add(new FrontMatterEntry(path, string.Empty, Array.Empty<string>(), i + 1));
            }
            else if (rest.StartsWith('[') && rest.EndsWith(']'))
            {
                entries.Add(new FrontMatterEntry(path, string.Empty, FlowSequence(rest), i + 1));
            }
            else if (rest.StartsWith('{') && rest.EndsWith('}'))
            {
                // A flow mapping's fields are not worth a second parser; the value is kept
                // verbatim and the key counts as filled in.
                entries.Add(new FrontMatterEntry(path, rest, Array.Empty<string>(), i + 1, IsMapping: true));
            }
            else
            {
                entries.Add(new FrontMatterEntry(path, Unquote(rest), Array.Empty<string>(), i + 1));
            }
        }

        return MarkMappingParents(entries);
    }

    // A key with no value of its own that other keys nest under is a mapping, not an unfilled
    // field — so `run:` above an indented `model: …` never reads as an empty value.
    private static IReadOnlyList<FrontMatterEntry> MarkMappingParents(List<FrontMatterEntry> entries)
    {
        // Every ancestor of a dotted key, not just its immediate parent, so a three-level
        // header marks both `run` and `run.model` as mappings.
        var prefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            for (var dot = entry.Key.IndexOf('.'); dot >= 0; dot = entry.Key.IndexOf('.', dot + 1))
                prefixes.Add(entry.Key[..dot]);
        }

        for (var i = 0; i < entries.Count; i++)
        {
            if (!entries[i].IsMapping && prefixes.Contains(entries[i].Key))
                entries[i] = entries[i] with { IsMapping = true };
        }
        return entries;
    }

    // The index of the `key:` separator, or -1 when the line is not a key line. A colon inside
    // a quoted key is skipped, and the separator must be followed by whitespace or end of line
    // so a bare `https://example.com` line is never mistaken for a key.
    private static int KeySeparator(string line)
    {
        var quote = '\0';
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                continue;
            }
            if (c is '"' or '\'') { quote = c; continue; }
            if (c != ':') continue;
            if (i == line.Length - 1 || line[i + 1] == ' ' || line[i + 1] == '\t') return i;
            return -1;
        }
        return -1;
    }

    // Guards against reading an ordinary prose line as metadata: a key is a single unquoted
    // word of identifier characters (or any quoted string).
    private static bool IsPlausibleKey(string name)
    {
        if (name.Length > 1 && (name[0] is '"' or '\'') && name[^1] == name[0]) return true;
        return name.All(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.' or '$' or '/');
    }

    private static IReadOnlyList<string> FlowSequence(string value) =>
        value[1..^1]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Unquote)
            .Where(v => v.Length != 0)
            .ToList();

    private static string Unquote(string value)
    {
        if (value.Length > 1 && (value[0] is '"' or '\'') && value[^1] == value[0])
            return value[1..^1];
        return value;
    }
}
