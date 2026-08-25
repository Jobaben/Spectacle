using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Spectacle.Checks;

namespace Spectacle.Gate;

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
/// The parser is a deliberate YAML *subset* — scalars (single-line, folded across lines, and
/// block scalars), quoted scalars, block and flow sequences including sequences of mappings, and
/// nested mappings by indentation. That covers every metadata header a generator emits
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
    ///
    /// Flat also means one line: a literal block scalar's value carries its own line breaks, and
    /// a pair that lands in a Markdown brief or a one-line-per-key listing would break the line
    /// it sits on. <see cref="FrontMatterEntry.Value"/> keeps the breaks for a consumer that
    /// wants the text itself.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> Metadata =>
        Entries.Where(e => !e.IsMapping)
            .Select(e => new KeyValuePair<string, string>(
                e.Key, FrontMatter.OneLine(e.IsList ? string.Join(", ", e.Items) : e.Value)))
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

        var i = from;
        while (i < toExclusive && i < lines.Length)
        {
            var raw = lines[i].TrimEnd('\r');
            var trimmed = raw.TrimStart();
            if (trimmed.Length == 0 || trimmed[0] == '#') { i++; continue; }

            var indent = raw.Length - trimmed.Length;

            // A block-sequence element belongs to the most recently declared key, and is read
            // whole: its continuation lines, and its fields when the element is a mapping.
            if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed == "-")
            {
                i = ReadSequenceElement(lines, i, toExclusive, indent, entries);
                continue;
            }

            var separator = KeySeparator(trimmed);
            if (separator < 0) { i++; continue; }

            var name = trimmed[..separator].Trim();
            if (name.Length == 0 || !IsPlausibleKey(name)) { i++; continue; }

            while (parents.Count != 0 && parents[^1].Indent >= indent) parents.RemoveAt(parents.Count - 1);
            var path = parents.Count == 0 ? name : parents[^1].Path + "." + name;

            var rest = trimmed[(separator + 1)..].Trim();
            var line = i + 1;
            i++;

            if (rest.Length == 0)
            {
                // Either a mapping parent or a block-sequence owner — which one only the
                // following lines reveal, so open it and let the post-pass decide.
                parents.Add((indent, path));
                entries.Add(new FrontMatterEntry(path, string.Empty, Array.Empty<string>(), line));
            }
            else if (BlockScalar(rest) is { } folded)
            {
                // The indicator is not the value: what follows it is. Consuming the body here is
                // also what keeps its prose from being read as structure.
                i = ReadBlockScalar(lines, i, toExclusive, indent, folded, out var text);
                entries.Add(new FrontMatterEntry(path, text, Array.Empty<string>(), line));
            }
            else if (rest.StartsWith('[') && rest.EndsWith(']'))
            {
                entries.Add(new FrontMatterEntry(path, string.Empty, FlowSequence(rest), line));
            }
            else if (rest.StartsWith('{') && rest.EndsWith('}'))
            {
                // A flow mapping's fields are not worth a second parser; the value is kept
                // verbatim and the key counts as filled in.
                entries.Add(new FrontMatterEntry(path, rest, Array.Empty<string>(), line, IsMapping: true));
            }
            else
            {
                i = ReadPlainScalar(lines, i, toExclusive, indent, Unquote(rest), out var text);
                entries.Add(new FrontMatterEntry(path, text, Array.Empty<string>(), line));
            }
        }

        return MarkMappingParents(entries);
    }

    /// <summary>
    /// Reads one block-sequence element into the most recently declared key and returns the index
    /// of the first line past it.
    ///
    /// An element resolves to a single item string: its continuation lines fold into it, and a
    /// mapping element's fields are joined with "; ". Keeping the fields inside the element is
    /// what makes a sequence of mappings — the shape the artifact-context policy prescribes —
    /// readable at all: hoisting them to dotted keys under the sequence key means every field the
    /// next element repeats overwrites the one before it, and marks the sequence itself a mapping,
    /// which drops its items from the metadata card entirely.
    /// </summary>
    private static int ReadSequenceElement(
        string[] lines, int i, int toExclusive, int indent, List<FrontMatterEntry> entries)
    {
        var trimmed = lines[i].TrimEnd('\r').TrimStart();
        var head = trimmed.Length > 1 ? trimmed[1..].Trim() : string.Empty;
        i++;

        var fields = new List<string>();
        // A field written on the dash line starts where its text does, two columns in.
        i = ReadElementField(lines, i, toExclusive, indent + 2, head, fields);

        while (i < toExclusive && i < lines.Length)
        {
            var raw = lines[i].TrimEnd('\r');
            var next = raw.TrimStart();
            if (next.Length == 0) { i++; continue; }

            var nextIndent = raw.Length - next.Length;
            if (nextIndent <= indent) break;
            if (next[0] == '#') { i++; continue; }
            // A nested sequence is past this subset. Leave the line to the outer loop rather than
            // folding structure into text.
            if (next.StartsWith("- ", StringComparison.Ordinal) || next == "-") break;

            i++;
            if (IsFieldLine(next))
                i = ReadElementField(lines, i, toExclusive, nextIndent, next, fields);
            else if (fields.Count != 0)
                fields[^1] = fields[^1].Length == 0 ? next : fields[^1] + " " + next;
            else
                fields.Add(next);
        }

        var item = string.Join("; ", fields.Where(f => f.Length != 0));
        if (item.Length != 0 && entries.Count != 0)
        {
            var last = entries[^1];
            entries[^1] = last with { Items = last.Items.Append(item).ToList() };
        }
        return i;
    }

    /// <summary>
    /// Appends one field of a sequence element — a plain line of text, or a <c>key: value</c> pair
    /// whose value may open a block scalar, whose body is then consumed. Returns the index of the
    /// first line past what it read.
    /// </summary>
    private static int ReadElementField(
        string[] lines, int i, int toExclusive, int contentIndent, string text, List<string> fields)
    {
        if (text.Length == 0) return i;
        if (!IsFieldLine(text))
        {
            fields.Add(text);
            return i;
        }

        var separator = KeySeparator(text);
        var name = text[..separator].Trim();
        var value = text[(separator + 1)..].Trim();

        if (value.Length == 0)
        {
            fields.Add(name + ":");
            return i;
        }

        if (BlockScalar(value) is { } folded)
        {
            i = ReadBlockScalar(lines, i, toExclusive, contentIndent, folded, out var scalar);
            fields.Add(scalar.Length == 0 ? name + ":" : name + ": " + OneLine(scalar));
            return i;
        }

        fields.Add(name + ": " + Unquote(value));
        return i;
    }

    /// <summary>
    /// Reads a block scalar's body: every following line indented past its key, blank lines
    /// included, because a paragraph break inside a scalar is content and must not end it. A
    /// folded scalar (<c>&gt;</c>) joins its lines with a space and turns a blank line into one
    /// break; a literal scalar (<c>|</c>) keeps every break. Returns the index of the first line
    /// past the body.
    /// </summary>
    private static int ReadBlockScalar(
        string[] lines, int i, int toExclusive, int ownerIndent, bool folded, out string text)
    {
        // A null entry is a blank line, held until the next content line decides what it means.
        var body = new List<string?>();
        var bodyIndent = -1;

        while (i < toExclusive && i < lines.Length)
        {
            var raw = lines[i].TrimEnd('\r');
            var trimmed = raw.TrimStart();
            if (trimmed.Length == 0) { body.Add(null); i++; continue; }

            var lineIndent = raw.Length - trimmed.Length;
            if (lineIndent <= ownerIndent) break;

            // The body's own indent is set by its first line; a deeper line keeps the extra, which
            // is what an indented list or code sample inside a scalar depends on.
            if (bodyIndent < 0) bodyIndent = lineIndent;
            body.Add(raw[Math.Min(bodyIndent, lineIndent)..]);
            i++;
        }

        // Blank lines past the last content line belong to the document, not to the scalar.
        while (body.Count != 0 && body[^1] is null) body.RemoveAt(body.Count - 1);

        var sb = new StringBuilder();
        var afterBlank = false;
        foreach (var entry in body)
        {
            if (entry is null) { afterBlank = true; continue; }
            if (sb.Length != 0) sb.Append(afterBlank ? (folded ? "\n" : "\n\n") : (folded ? " " : "\n"));
            sb.Append(entry.TrimEnd());
            afterBlank = false;
        }

        text = sb.ToString();
        return i;
    }

    /// <summary>
    /// Reads the continuation lines of a plain scalar — the indented prose beneath a key that
    /// already holds a value, which YAML folds into it and which was silently truncated when it
    /// was dropped. A blank line ends the scalar; a line that reads as a key or a sequence item is
    /// left to the caller, so what a nested key means does not change.
    /// </summary>
    private static int ReadPlainScalar(
        string[] lines, int i, int toExclusive, int ownerIndent, string head, out string text)
    {
        var sb = new StringBuilder(head);
        while (i < toExclusive && i < lines.Length)
        {
            var raw = lines[i].TrimEnd('\r');
            var trimmed = raw.TrimStart();
            if (trimmed.Length == 0) break;

            var lineIndent = raw.Length - trimmed.Length;
            if (lineIndent <= ownerIndent) break;
            if (trimmed[0] == '#') break;
            if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed == "-") break;
            if (IsFieldLine(trimmed)) break;

            sb.Append(' ').Append(trimmed);
            i++;
        }

        text = sb.ToString();
        return i;
    }

    // The block-scalar indicator: `|` or `>` with an optional indentation digit and chomping
    // marker. True for a folded scalar, false for a literal one, null when the value is ordinary
    // text — `note: > 5 items` holds a value, not an indicator. Chomping past dropping the
    // trailing break is not modelled: a metadata card shows text, not byte-exact YAML.
    private static bool? BlockScalar(string value)
    {
        if (value.Length == 0 || (value[0] != '|' && value[0] != '>')) return null;
        for (var i = 1; i < value.Length; i++)
        {
            if (!char.IsDigit(value[i]) && value[i] != '-' && value[i] != '+') return null;
        }
        return value[0] == '>';
    }

    // Whether a line inside a sequence element declares one of its fields, as opposed to being
    // more of the element's own text: `decision: …` does, `see also: the design` does not, since
    // a key is a single word.
    private static bool IsFieldLine(string line)
    {
        var separator = KeySeparator(line);
        return separator > 0 && IsPlausibleKey(line[..separator].Trim());
    }

    /// <summary>
    /// <paramref name="text"/> on a single line — what a display pair and a sequence element both
    /// need, since a block scalar's breaks would otherwise split the line they land on.
    /// </summary>
    internal static string OneLine(string text) =>
        text.Contains('\n')
            ? string.Join(" ", text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()))
            : text;

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
