using System;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Spectacle.Annotations;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class RevisionPlanGeneratorTests
{
    private static readonly DateTime FixedNow = new(2026, 5, 15, 10, 0, 0, DateTimeKind.Utc);

    // Anchor a comment to a real block of the rendered content so it actually
    // matches (matching keys on Kind + TextHash + OccurrenceIndex).
    private static AnnotationFile FileWithCommentOn(string content, string kind, string body)
    {
        var block = new MdRenderer().Render(content).Blocks.First(b => b.Kind == kind);
        var anchor = new BlockAnchor(block.Kind, block.Line, block.TextHash, block.OccurrenceIndex, block.OriginalText);
        var comment = new Comment("c1", anchor, block.OriginalText, body, FixedNow, null);
        return new AnnotationFile(1, @"C:\doc.md", "", new[] { comment });
    }

    [Fact]
    public void Markdown_format_renders_matched_comment_into_plan()
    {
        const string content = "# Title\n\nSome paragraph text.\n";
        var file = FileWithCommentOn(content, "paragraph", "Tighten this up.");

        // Plain filename so the header assertion holds on any OS (Path.GetFileName
        // only treats '\' as a separator on Windows).
        var plan = RevisionPlanGenerator.Generate(
            "doc.md", content, file, FixedNow, RevisionPlanFormat.Markdown);

        plan.Should().Contain("# Revision plan for doc.md");
        plan.Should().Contain("> Some paragraph text.");
        plan.Should().Contain("Tighten this up.");
    }

    [Fact]
    public void Json_format_renders_matched_comment_into_structured_plan()
    {
        const string content = "# Title\n\nSome paragraph text.\n";
        var file = FileWithCommentOn(content, "paragraph", "Tighten this up.");

        var json = RevisionPlanGenerator.Generate(
            @"C:\doc.md", content, file, FixedNow, RevisionPlanFormat.Json);

        var root = JsonDocument.Parse(json).RootElement;
        root.GetProperty("revisionCount").GetInt32().Should().Be(1);
        var rev = root.GetProperty("revisions")[0];
        rev.GetProperty("original").GetString().Should().Be("Some paragraph text.");
        rev.GetProperty("instruction").GetString().Should().Be("Tighten this up.");
    }

    [Fact]
    public void Computes_source_sha256_over_content()
    {
        const string content = "# Hello\n";
        var json = RevisionPlanGenerator.Generate(
            @"C:\doc.md", content, new AnnotationFile(1, @"C:\doc.md", "", Array.Empty<Comment>()),
            FixedNow, RevisionPlanFormat.Json);

        // SHA-256 of "# Hello\n" — 64 lowercase hex chars, deterministic.
        var sha = JsonDocument.Parse(json).RootElement.GetProperty("sourceSha256").GetString();
        sha.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Non_matching_comment_is_dropped_as_orphan()
    {
        const string content = "# Title\n\nReal paragraph.\n";
        var staleAnchor = new BlockAnchor("paragraph", 99, "no-such-hash", 0, "Deleted text.");
        var comment = new Comment("c1", staleAnchor, "Deleted text.", "Stale", FixedNow, null);
        var file = new AnnotationFile(1, @"C:\doc.md", "", new[] { comment });

        var json = RevisionPlanGenerator.Generate(
            @"C:\doc.md", content, file, FixedNow, RevisionPlanFormat.Json);

        JsonDocument.Parse(json).RootElement.GetProperty("revisionCount").GetInt32().Should().Be(0);
    }
}
