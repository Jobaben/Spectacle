using System.Collections.Generic;
using Spectacle.Checks;

namespace Spectacle.Render;

public sealed record RenderResult(
    string Html,
    IReadOnlyList<TaggedBlock> Blocks,
    IReadOnlyList<OutlineEntry> Outline);
