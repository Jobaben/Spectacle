using System.Collections.Generic;
using System.Linq;

namespace Spectacle.Render;

/// <summary>One block that was added or removed between two spec versions.</summary>
public sealed record DiffEntry(string Kind, int Line, string Text);

/// <summary>Blocks that differ between a base and a revised document.</summary>
public sealed record DiffResult(IReadOnlyList<DiffEntry> Added, IReadOnlyList<DiffEntry> Removed);

/// <summary>
/// A structural, block-level diff between two Markdown specs — useful for
/// reviewing what an AI agent changed between iterations. Blocks are identified
/// by the same (kind, text-hash, occurrence) key the annotation matcher uses,
/// so a block counts as unchanged only if its normalized text is identical.
/// An edit therefore shows up as one removed + one added block (this is a
/// presence diff, not a line-level word diff).
/// </summary>
public static class SpecDiff
{
    public static DiffResult Compare(string baseContent, string revisedContent)
    {
        var baseBlocks = new MdRenderer().Render(baseContent).Blocks;
        var revisedBlocks = new MdRenderer().Render(revisedContent).Blocks;

        var baseKeys = baseBlocks.Select(Key).ToHashSet();
        var revisedKeys = revisedBlocks.Select(Key).ToHashSet();

        var added = revisedBlocks.Where(b => !baseKeys.Contains(Key(b))).Select(ToEntry).ToList();
        var removed = baseBlocks.Where(b => !revisedKeys.Contains(Key(b))).Select(ToEntry).ToList();

        return new DiffResult(added, removed);
    }

    private static (string, string, int) Key(TaggedBlock b) => (b.Kind, b.TextHash, b.OccurrenceIndex);

    private static DiffEntry ToEntry(TaggedBlock b) => new(b.Kind, b.Line, b.OriginalText);
}
