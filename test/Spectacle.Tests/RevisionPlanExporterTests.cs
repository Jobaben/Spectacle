using System;
using FluentAssertions;
using Spectacle.Annotations;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class RevisionPlanExporterTests
{
    private static readonly DateTime FixedNow = new(2026, 5, 15, 10, 0, 0, DateTimeKind.Utc);

    private static MatchedComment MatchOf(string kind, int line, string body, string original)
    {
        var anchor = new BlockAnchor(kind, line, "h", 0, original);
        var c = new Comment("c", anchor, original, body, FixedNow, null);
        var b = new TaggedBlock("b0", kind, line, "h", 0, original);
        return new MatchedComment(c, b);
    }

    [Fact]
    public void Header_contains_source_path_and_sha()
    {
        var plan = RevisionPlanExporter.Build(
            sourcePath: @"C:\path\README.md",
            sourceSha256: "abc123",
            generatedAt: FixedNow,
            matched: System.Array.Empty<MatchedComment>());

        plan.Should().Contain(@"C:\path\README.md");
        plan.Should().Contain("SHA-256: abc123");
        plan.Should().Contain("2026-05-15T10:00:00");
    }

    [Fact]
    public void Single_revision_quotes_original_and_includes_instruction()
    {
        var match = MatchOf("paragraph", 42, "Reword for clarity.", "Spectacle is a viewer.");

        var plan = RevisionPlanExporter.Build(
            @"C:\R.md", "h", FixedNow, new[] { match });

        plan.Should().Contain("## Revision 1 — paragraph at line 42");
        plan.Should().Contain("> Spectacle is a viewer.");
        plan.Should().Contain("Reword for clarity.");
    }

    [Fact]
    public void Multi_line_original_is_quoted_with_per_line_prefix()
    {
        var match = MatchOf("code", 5, "explain this", "line one\nline two\nline three");

        var plan = RevisionPlanExporter.Build(@"C:\R.md", "h", FixedNow, new[] { match });

        plan.Should().Contain("> line one");
        plan.Should().Contain("> line two");
        plan.Should().Contain("> line three");
    }

    [Fact]
    public void Revisions_are_numbered_in_input_order()
    {
        var a = MatchOf("paragraph", 1, "first", "A");
        var b = MatchOf("paragraph", 2, "second", "B");
        var c = MatchOf("paragraph", 3, "third", "C");

        var plan = RevisionPlanExporter.Build(@"C:\R.md", "h", FixedNow, new[] { a, b, c });

        var idx1 = plan.IndexOf("## Revision 1");
        var idx2 = plan.IndexOf("## Revision 2");
        var idx3 = plan.IndexOf("## Revision 3");
        idx1.Should().BeLessThan(idx2);
        idx2.Should().BeLessThan(idx3);
    }

    [Fact]
    public void Empty_matched_produces_header_with_no_revisions()
    {
        var plan = RevisionPlanExporter.Build(@"C:\R.md", "h", FixedNow, System.Array.Empty<MatchedComment>());

        plan.Should().Contain("# Revision plan");
        plan.Should().NotContain("## Revision");
    }
}
