using System;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Spectacle.Annotations;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class ReviewSummaryTests
{
    private static readonly DateTime FixedNow = new(2026, 5, 15, 10, 0, 0, DateTimeKind.Utc);

    private static Comment AnchoredComment(string content, string kind, string id, DateTime? resolvedAt)
    {
        var block = new MdRenderer().Render(content).Blocks.First(b => b.Kind == kind);
        var anchor = new BlockAnchor(block.Kind, block.Line, block.TextHash, block.OccurrenceIndex, block.OriginalText);
        return new Comment(id, anchor, block.OriginalText, "note", FixedNow, resolvedAt);
    }

    [Fact]
    public void Empty_annotations_count_zero()
    {
        var file = new AnnotationFile(1, @"C:\doc.md", "", Array.Empty<Comment>());

        var s = ReviewSummary.Compute("# Title\n", file);

        s.Total.Should().Be(0);
        s.Open.Should().Be(0);
        s.Resolved.Should().Be(0);
        s.Matched.Should().Be(0);
        s.Orphaned.Should().Be(0);
    }

    [Fact]
    public void Counts_open_resolved_matched_and_orphaned()
    {
        const string content = "# Heading text\n\nParagraph text.\n";
        var open = AnchoredComment(content, "heading", "h", null);
        var resolved = AnchoredComment(content, "paragraph", "p",
            new DateTime(2026, 5, 16, 0, 0, 0, DateTimeKind.Utc));
        // Anchor no longer present in the document -> orphaned.
        var orphan = new Comment("o",
            new BlockAnchor("paragraph", 99, "gone-hash", 0, "Deleted"), "Deleted", "note", FixedNow, null);
        var file = new AnnotationFile(1, @"C:\doc.md", "", new[] { open, resolved, orphan });

        var s = ReviewSummary.Compute(content, file);

        s.Total.Should().Be(3);
        s.Open.Should().Be(2);       // open + orphan (neither resolved)
        s.Resolved.Should().Be(1);
        s.Matched.Should().Be(2);    // open + resolved still anchor to blocks
        s.Orphaned.Should().Be(1);
    }

    [Fact]
    public void Text_format_reports_each_count()
    {
        var s = new ReviewSummary(Total: 5, Open: 3, Resolved: 2, Matched: 4, Orphaned: 1);

        var text = ReviewSummaryExporter.Build(s, @"C:\path\spec.md", FixedNow, RevisionPlanFormat.Markdown);

        text.Should().Contain("spec.md");
        text.Should().Contain("5");
        text.Should().Contain("Open");
        text.Should().Contain("Resolved");
        text.Should().Contain("Orphaned");
    }

    [Fact]
    public void Json_format_emits_structured_counts()
    {
        var s = new ReviewSummary(Total: 5, Open: 3, Resolved: 2, Matched: 4, Orphaned: 1);

        var json = ReviewSummaryExporter.Build(s, @"C:\path\spec.md", FixedNow, RevisionPlanFormat.Json);

        var root = JsonDocument.Parse(json).RootElement;
        root.GetProperty("source").GetString().Should().Be(@"C:\path\spec.md");
        root.GetProperty("generatedAt").GetString().Should().Be("2026-05-15T10:00:00Z");
        root.GetProperty("total").GetInt32().Should().Be(5);
        root.GetProperty("open").GetInt32().Should().Be(3);
        root.GetProperty("resolved").GetInt32().Should().Be(2);
        root.GetProperty("matched").GetInt32().Should().Be(4);
        root.GetProperty("orphaned").GetInt32().Should().Be(1);
    }
}
