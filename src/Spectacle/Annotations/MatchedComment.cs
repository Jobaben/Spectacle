using System.Collections.Generic;
using Spectacle.Checks;

namespace Spectacle.Annotations;

public sealed record MatchedComment(Comment Comment, TaggedBlock CurrentBlock);

public sealed record MatchResult(
    IReadOnlyList<MatchedComment> Matched,
    IReadOnlyList<Comment> Orphaned);
