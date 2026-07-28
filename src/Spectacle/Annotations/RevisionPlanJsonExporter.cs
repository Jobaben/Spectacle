using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Spectacle.Annotations;

/// <summary>
/// Renders a revision plan as structured JSON for an AI agent to apply
/// programmatically. Mirrors <see cref="RevisionPlanExporter"/> (which produces
/// the prose form); both take the same matched comments so the two formats stay
/// in lockstep. Timestamps are emitted as ISO-8601 UTC (<c>...Z</c>).
/// </summary>
public static class RevisionPlanJsonExporter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Relaxed escaping keeps prose like `<tag>` and `"quotes"` readable in
        // the output instead of \u00XX-encoded; the result is still valid JSON.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private const string Iso8601Utc = "yyyy-MM-ddTHH:mm:ssZ";

    public static string Build(
        string sourcePath,
        string sourceSha256,
        DateTime generatedAt,
        IReadOnlyList<MatchedComment> matched)
    {
        var revisions = new List<Revision>(matched.Count);
        for (var i = 0; i < matched.Count; i++)
        {
            var m = matched[i];
            revisions.Add(new Revision(
                Index: i + 1,
                CommentId: m.Comment.Id,
                Kind: m.Comment.BlockAnchor.Kind,
                Line: m.CurrentBlock.Line,
                Original: m.Comment.OriginalText,
                Instruction: m.Comment.Body,
                CreatedAt: Stamp(m.Comment.CreatedAt),
                Resolved: m.Comment.ResolvedAt is not null,
                ResolvedAt: m.Comment.ResolvedAt is { } r ? Stamp(r) : null));
        }

        var plan = new Plan(
            Source: sourcePath,
            SourceSha256: sourceSha256,
            GeneratedAt: Stamp(generatedAt),
            RevisionCount: revisions.Count,
            Revisions: revisions);

        return JsonSerializer.Serialize(plan, Options);
    }

    private static string Stamp(DateTime value) =>
        value.ToUniversalTime().ToString(Iso8601Utc);

    private sealed record Plan(
        string Source,
        string SourceSha256,
        string GeneratedAt,
        int RevisionCount,
        IReadOnlyList<Revision> Revisions);

    private sealed record Revision(
        int Index,
        string CommentId,
        string Kind,
        int Line,
        string Original,
        string Instruction,
        string CreatedAt,
        bool Resolved,
        string? ResolvedAt);
}
