using System.Collections.Generic;
using Spectacle.Render;

namespace Spectacle.Annotations;

public sealed record MatchedComment(Comment Comment, TaggedBlock CurrentBlock);

public sealed record MatchResult(
    IReadOnlyList<MatchedComment> Matched,
    IReadOnlyList<Comment> Orphaned);
