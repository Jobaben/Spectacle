using System.Collections.Generic;

namespace Spectacle.Render;

public sealed record RenderResult(
    string Html,
    IReadOnlyList<TaggedBlock> Blocks,
    IReadOnlyList<OutlineEntry> Outline);
