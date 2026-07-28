using System.Collections.Generic;

namespace Spectacle.Render;

/// <summary>
/// One block that repeats content already present earlier in the spec, carrying the
/// duplicate's 1-based line, the line of the first (original) occurrence, the block
/// kind, and the normalized text.
/// </summary>
public sealed record DuplicateBlock(string Kind, int Line, int FirstLine, string Text);

/// <summary>
/// Repetition check: flags blocks whose normalized text is identical to a block that
/// already appeared earlier in the spec. AI agents routinely emit the same paragraph,
/// list item, or code block twice — boilerplate they pasted into two sections, or a
/// requirement restated verbatim — and none of the other checks notice, because each
/// validates a block in isolation.
///
/// Matching is exact after the same whitespace normalization the block tagger applies
/// (so trailing spaces and line-ending differences don't hide a duplicate), keyed by
/// (kind, text) — a heading that matches a paragraph's text is not a duplicate. Blocks
/// shorter than <see cref="MinLength"/> and thematic breaks are ignored: short lines
/// (separators, "N/A", a one-word label) repeat legitimately and would only be noise.
/// </summary>
public static class DuplicateBlockChecker
{
    /// <summary>
    /// Minimum normalized-text length for a block to be considered. Blocks shorter than
    /// this repeat too often by design (labels, single words) to be meaningful duplicates.
    /// </summary>
    public const int MinLength = 24;

    public static IReadOnlyList<DuplicateBlock> Check(string? content)
    {
        var blocks = new MdRenderer().Render(content ?? string.Empty).Blocks;

        // First line each (kind, text-hash) was seen on; a later block with the same key
        // is a duplicate of it. Blocks arrive in document order, so the first store wins.
        var firstLine = new Dictionary<(string Kind, string Hash), int>();
        var duplicates = new List<DuplicateBlock>();

        foreach (var b in blocks)
        {
            // Thematic breaks (horizontal rules) are structurally repeated by design.
            if (b.Kind == "hr") continue;
            if (b.OriginalText.Length < MinLength) continue;

            var key = (b.Kind, b.TextHash);
            if (firstLine.TryGetValue(key, out var first))
                duplicates.Add(new DuplicateBlock(b.Kind, b.Line, first, b.OriginalText));
            else
                firstLine[key] = b.Line;
        }

        return duplicates;
    }
}
