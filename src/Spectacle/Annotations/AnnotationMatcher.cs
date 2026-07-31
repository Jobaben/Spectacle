using System.Collections.Generic;
using System.Linq;
using Spectacle.Render;

namespace Spectacle.Annotations;

public static class AnnotationMatcher
{
    public static MatchResult Match(
        IReadOnlyList<TaggedBlock> currentBlocks,
        IReadOnlyList<Comment> savedComments)
    {
        var byKey = currentBlocks.ToDictionary(
            b => (b.Kind, b.TextHash, b.OccurrenceIndex),
            b => b);

        var matched = new List<MatchedComment>();
        var orphaned = new List<Comment>();

        foreach (var comment in savedComments)
        {
            var key = (
                comment.BlockAnchor.Kind,
                comment.BlockAnchor.TextHash,
                comment.BlockAnchor.OccurrenceIndex);

            if (byKey.TryGetValue(key, out var block))
                matched.Add(new MatchedComment(comment, block));
            else
                orphaned.Add(comment);
        }

        return new MatchResult(matched, orphaned);
    }
}
