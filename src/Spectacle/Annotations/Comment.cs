using System;

namespace Spectacle.Annotations;

public sealed record Comment(
    string Id,
    BlockAnchor BlockAnchor,
    string OriginalText,
    string Body,
    DateTime CreatedAt,
    DateTime? ResolvedAt);
