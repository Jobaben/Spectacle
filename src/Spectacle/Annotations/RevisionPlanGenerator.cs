using System;
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
        RevisionPlanFormat format)
    {
        var blocks = new MdRenderer().Render(content).Blocks;
        var match = AnnotationMatcher.Match(blocks, annotations.Comments);
        var sha = Sha256Hex(content);

        return format switch
        {
            RevisionPlanFormat.Json =>
                RevisionPlanJsonExporter.Build(sourcePath, sha, generatedAt, match.Matched),
            _ =>
                RevisionPlanExporter.Build(sourcePath, sha, generatedAt, match.Matched),
        };
    }

    private static string Sha256Hex(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
}
