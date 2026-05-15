namespace Spectacle.Render;

public sealed record TaggedBlock(
    string BlockId,
    string Kind,
    int Line,
    string TextHash,
    int OccurrenceIndex,
    string OriginalText);
