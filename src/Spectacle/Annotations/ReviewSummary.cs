using System.Linq;
using Spectacle.Render;

namespace Spectacle.Annotations;

/// <summary>
/// A headless snapshot of where a review stands for a document: how many
/// comments exist, how many are still open vs resolved, and how many still
/// anchor to a current block (<c>Matched</c>) vs point at content that has
/// since changed or been removed (<c>Orphaned</c> — typically because the AI
/// agent already edited that part of the spec).
/// </summary>
public sealed record ReviewSummary(int Total, int Open, int Resolved, int Matched, int Orphaned)
{
    public static ReviewSummary Compute(string content, AnnotationFile annotations)
    {
        var blocks = new MdRenderer().Render(content).Blocks;
        var match = AnnotationMatcher.Match(blocks, annotations.Comments);

        var total = annotations.Comments.Count;
        var resolved = annotations.Comments.Count(c => c.ResolvedAt is not null);

        return new ReviewSummary(
            Total: total,
            Open: total - resolved,
            Resolved: resolved,
            Matched: match.Matched.Count,
            Orphaned: match.Orphaned.Count);
    }
}
