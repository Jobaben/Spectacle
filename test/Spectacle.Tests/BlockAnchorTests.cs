using System;
using System.Text.Json;
using FluentAssertions;
using Spectacle.Annotations;
using Xunit;

namespace Spectacle.Tests;

public class BlockAnchorTests
{
    [Fact]
    public void BlockAnchor_round_trips_through_json()
    {
        var anchor = new BlockAnchor(
            Kind: "paragraph",
            Line: 42,
            TextHash: "abc123",
            OccurrenceIndex: 0,
            LeadingText: "Spectacle is");

        var json = JsonSerializer.Serialize(anchor);
        var clone = JsonSerializer.Deserialize<BlockAnchor>(json);

        clone.Should().Be(anchor);
    }

    [Fact]
    public void Comment_round_trips_through_json()
    {
        var anchor = new BlockAnchor("paragraph", 1, "h", 0, "lead");
        var comment = new Comment(
            Id: "id-1",
            BlockAnchor: anchor,
            OriginalText: "Hello.",
            Body: "Reword",
            CreatedAt: new DateTime(2026, 5, 15, 10, 0, 0, DateTimeKind.Utc),
            ResolvedAt: null);

        var json = JsonSerializer.Serialize(comment);
        var clone = JsonSerializer.Deserialize<Comment>(json);

        clone.Should().Be(comment);
    }

    [Fact]
    public void AnnotationFile_round_trips_through_json()
    {
        var file = new AnnotationFile(
            FileVersion: 1,
            SourcePath: @"C:\path\README.md",
            SourceHashAtWrite: "abc",
            Comments: new[]
            {
                new Comment("c1",
                    new BlockAnchor("heading", 1, "h", 0, "Hi"),
                    "# Hi", "rename",
                    new DateTime(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc),
                    null)
            });

        var json = JsonSerializer.Serialize(file);
        var clone = JsonSerializer.Deserialize<AnnotationFile>(json);

        clone.Should().Be(file);
    }
}
