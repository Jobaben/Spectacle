using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Spectacle.Render;

namespace Spectacle.Annotations;

/// <summary>
/// Produces a revision plan from a source document and its saved annotations,
/// without any UI. Re-renders the current source, re-anchors saved comments
/// against it (dropping orphans whose blocks no longer exist), and emits the
/// plan in the requested <see cref="RevisionPlanFormat"/>. Pure logic: callers
/// supply the already-read content and loaded annotations, so this is the same
/// path the GUI's "Copy/Export revision plan" takes, made headless.
/// </summary>
public static class RevisionPlanGenerator
{
    public static string Generate(
        string sourcePath,
        string content,
        AnnotationFile annotations,
        DateTime generatedAt,
        RevisionPlanFormat format,
        bool unresolvedOnly = false)
    {
        var blocks = new MdRenderer().Render(content).Blocks;
        var match = AnnotationMatcher.Match(blocks, annotations.Comments);
        var sha = Sha256Hex(content);

        var revisions = unresolvedOnly
            ? match.Matched.Where(m => m.Comment.ResolvedAt is null).ToList()
            : (IReadOnlyList<MatchedComment>)match.Matched;

        return format switch
        {
            RevisionPlanFormat.Json =>
                RevisionPlanJsonExporter.Build(sourcePath, sha, generatedAt, revisions),
            _ =>
                RevisionPlanExporter.Build(sourcePath, sha, generatedAt, revisions),
        };
    }

    private static string Sha256Hex(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
}
