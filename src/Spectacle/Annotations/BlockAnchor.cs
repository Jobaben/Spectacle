namespace Spectacle.Annotations;

public sealed record BlockAnchor(
    string Kind,
    int Line,
    string TextHash,
    int OccurrenceIndex,
    string LeadingText);
