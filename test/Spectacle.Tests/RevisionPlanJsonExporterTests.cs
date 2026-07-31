using System;
using System.Text.Json;
using FluentAssertions;
using Spectacle.Annotations;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class RevisionPlanJsonExporterTests
{
    private static readonly DateTime FixedNow = new(2026, 5, 15, 10, 0, 0, DateTimeKind.Utc);

    private static MatchedComment MatchOf(
        string id, string kind, int line, string body, string original,
        DateTime? resolvedAt = null)
    {
        var anchor = new BlockAnchor(kind, line, "h", 0, original);
        var c = new Comment(id, anchor, original, body, FixedNow, resolvedAt);
        var b = new TaggedBlock("b0", kind, line, "h", 0, original);
        return new MatchedComment(c, b);
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Emits_valid_json_with_header_fields()
    {
        var json = RevisionPlanJsonExporter.Build(
            @"C:\path\README.md", "abc123", FixedNow, Array.Empty<MatchedComment>());

        var root = Parse(json);
        root.GetProperty("source").GetString().Should().Be(@"C:\path\README.md");
        root.GetProperty("sourceSha256").GetString().Should().Be("abc123");
        root.GetProperty("generatedAt").GetString().Should().Be("2026-05-15T10:00:00Z");
        root.GetProperty("revisionCount").GetInt32().Should().Be(0);
        root.GetProperty("revisions").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void Revision_carries_structured_fields()
    {
        var match = MatchOf("c1", "paragraph", 42, "Reword for clarity.", "Spectacle is a viewer.");

        var json = RevisionPlanJsonExporter.Build(@"C:\R.md", "h", FixedNow, new[] { match });

        var rev = Parse(json).GetProperty("revisions")[0];
        rev.GetProperty("index").GetInt32().Should().Be(1);
        rev.GetProperty("commentId").GetString().Should().Be("c1");
        rev.GetProperty("kind").GetString().Should().Be("paragraph");
        rev.GetProperty("line").GetInt32().Should().Be(42);
        rev.GetProperty("original").GetString().Should().Be("Spectacle is a viewer.");
        rev.GetProperty("instruction").GetString().Should().Be("Reword for clarity.");
        rev.GetProperty("resolved").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void Indexes_are_one_based_and_sequential()
    {
        var json = RevisionPlanJsonExporter.Build(@"C:\R.md", "h", FixedNow, new[]
        {
            MatchOf("a", "heading", 1, "x", "A"),
            MatchOf("b", "paragraph", 2, "y", "B"),
            MatchOf("c", "code", 3, "z", "C"),
        });

        var revs = Parse(json).GetProperty("revisions");
        revs.GetArrayLength().Should().Be(3);
        revs[0].GetProperty("index").GetInt32().Should().Be(1);
        revs[2].GetProperty("index").GetInt32().Should().Be(3);
        Parse(json).GetProperty("revisionCount").GetInt32().Should().Be(3);
    }

    [Fact]
    public void Resolved_comment_reports_resolved_true_and_timestamp()
    {
        var resolvedAt = new DateTime(2026, 5, 16, 9, 30, 0, DateTimeKind.Utc);
        var match = MatchOf("c1", "paragraph", 7, "done", "text", resolvedAt);

        var rev = Parse(RevisionPlanJsonExporter.Build(@"C:\R.md", "h", FixedNow, new[] { match }))
            .GetProperty("revisions")[0];

        rev.GetProperty("resolved").GetBoolean().Should().BeTrue();
        rev.GetProperty("resolvedAt").GetString().Should().Be("2026-05-16T09:30:00Z");
    }

    [Fact]
    public void Multiline_and_special_characters_round_trip_intact()
    {
        var match = MatchOf("c1", "code", 5, "explain \"this\" <tag>", "line one\nline two");

        var rev = Parse(RevisionPlanJsonExporter.Build(@"C:\R.md", "h", FixedNow, new[] { match }))
            .GetProperty("revisions")[0];

        rev.GetProperty("original").GetString().Should().Be("line one\nline two");
        rev.GetProperty("instruction").GetString().Should().Be("explain \"this\" <tag>");
    }
}
