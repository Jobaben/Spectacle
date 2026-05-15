using System;
using System.Collections.Generic;
using FluentAssertions;
using Spectacle.Annotations;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class AnnotationMatcherTests
{
    private static Comment MakeComment(string kind, string textHash, int occ, string body = "x") =>
        new(
            Id: Guid.NewGuid().ToString(),
            BlockAnchor: new BlockAnchor(kind, Line: 1, TextHash: textHash,
                OccurrenceIndex: occ, LeadingText: "lead"),
            OriginalText: "orig",
            Body: body,
            CreatedAt: new DateTime(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc),
            ResolvedAt: null);

    private static TaggedBlock MakeBlock(string id, string kind, int line, string hash, int occ) =>
        new(id, kind, line, hash, occ, OriginalText: "orig");

    [Fact]
    public void Matches_when_kind_hash_and_occurrence_align()
    {
        var c = MakeComment("paragraph", "h1", 0);
        var b = MakeBlock("b0", "paragraph", 5, "h1", 0);

        var result = AnnotationMatcher.Match(new[] { b }, new[] { c });

        result.Matched.Should().ContainSingle();
        result.Matched[0].Comment.Should().Be(c);
        result.Matched[0].CurrentBlock.Should().Be(b);
        result.Orphaned.Should().BeEmpty();
    }

    [Fact]
    public void Orphans_when_text_hash_changes()
    {
        var c = MakeComment("paragraph", "old-hash", 0);
        var b = MakeBlock("b0", "paragraph", 1, "new-hash", 0);

        var result = AnnotationMatcher.Match(new[] { b }, new[] { c });

        result.Matched.Should().BeEmpty();
        result.Orphaned.Should().ContainSingle().Which.Should().Be(c);
    }

    [Fact]
    public void Orphans_when_kind_changes()
    {
        var c = MakeComment("paragraph", "h", 0);
        var b = MakeBlock("b0", "heading", 1, "h", 0);

        var result = AnnotationMatcher.Match(new[] { b }, new[] { c });

        result.Orphaned.Should().ContainSingle();
    }

    [Fact]
    public void Orphans_when_occurrence_index_no_longer_exists()
    {
        var c = MakeComment("paragraph", "h", 1); // wanted the 2nd; only 1 exists
        var b = MakeBlock("b0", "paragraph", 1, "h", 0);

        var result = AnnotationMatcher.Match(new[] { b }, new[] { c });

        result.Orphaned.Should().ContainSingle();
    }

    [Fact]
    public void Survives_line_shift_when_kind_hash_occurrence_unchanged()
    {
        var c = MakeComment("paragraph", "h", 0);
        var b = MakeBlock("b0", "paragraph", 99, "h", 0);

        var result = AnnotationMatcher.Match(new[] { b }, new[] { c });

        result.Matched.Should().ContainSingle();
    }

    [Fact]
    public void Two_identical_blocks_two_comments_each_one_matches_its_own()
    {
        var c1 = MakeComment("paragraph", "h", 0, body: "first");
        var c2 = MakeComment("paragraph", "h", 1, body: "second");
        var b1 = MakeBlock("b0", "paragraph", 1, "h", 0);
        var b2 = MakeBlock("b1", "paragraph", 5, "h", 1);

        var result = AnnotationMatcher.Match(new[] { b1, b2 }, new[] { c1, c2 });

        result.Matched.Should().HaveCount(2);
        result.Orphaned.Should().BeEmpty();
        result.Matched.Should().Contain(m => m.Comment.Body == "first" && m.CurrentBlock.BlockId == "b0");
        result.Matched.Should().Contain(m => m.Comment.Body == "second" && m.CurrentBlock.BlockId == "b1");
    }

    [Fact]
    public void Block_deleted_orphans_its_comment()
    {
        var c = MakeComment("paragraph", "h", 0);

        var result = AnnotationMatcher.Match(Array.Empty<TaggedBlock>(), new[] { c });

        result.Orphaned.Should().ContainSingle();
    }

    [Fact]
    public void Block_inserted_above_does_not_disturb_existing_match()
    {
        var c = MakeComment("paragraph", "target", 0);
        var inserted = MakeBlock("b0", "paragraph", 1, "new", 0);
        var target = MakeBlock("b1", "paragraph", 3, "target", 0);

        var result = AnnotationMatcher.Match(new[] { inserted, target }, new[] { c });

        result.Matched.Should().ContainSingle()
            .Which.CurrentBlock.Should().Be(target);
    }
}
