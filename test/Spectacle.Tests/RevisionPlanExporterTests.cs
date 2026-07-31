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

    [Fact]
    public void Heading_revision_includes_leading_text_in_qualifier()
    {
        var match = MatchOf("heading", 7, "rename", "## Install");
        var plan = RevisionPlanExporter.Build(@"C:\R.md", "h", FixedNow, new[] { match });

        plan.Should().Contain("## Revision 1 — heading at line 7 (\"## Install\")");
    }

    [Fact]
    public void Crlf_original_quotes_each_line_without_stray_cr()
    {
        var match = MatchOf("paragraph", 1, "x", "alpha\r\nbeta");
        var plan = RevisionPlanExporter.Build(@"C:\R.md", "h", FixedNow, new[] { match });

        plan.Should().Contain("> alpha");
        plan.Should().Contain("> beta");
        // StringBuilder.AppendLine emits Environment.NewLine ("\r\n" on Windows),
        // so a substring like "> alpha\r" is ambiguous (the trailing \r could be
        // the legitimate line terminator or a stray CR from CRLF input).
        // Normalize legitimate Windows newlines away first, then assert no CRs
        // from the CRLF input survived.
        plan.Replace("\r\n", "\n").Should().NotContain("\r");
    }

    [Fact]
    public void Generated_timestamp_coerces_local_time_to_utc()
    {
        var local = new DateTime(2026, 5, 15, 12, 0, 0, DateTimeKind.Local);
        var plan = RevisionPlanExporter.Build(@"C:\R.md", "h", local,
            System.Array.Empty<MatchedComment>());

        plan.Should().Contain("Generated: " + local.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"));
    }

    [Fact]
    public void Golden_snapshot_three_comments_across_kinds()
    {
        var anchorP = new BlockAnchor("paragraph", 12, "h1", 0, "Spectacle is a viewer.");
        var anchorH = new BlockAnchor("heading", 7, "h2", 0, "## Install");
        var anchorC = new BlockAnchor("code", 30, "h3", 0, "var x = 1;\nvar y = 2;");

        var commentP = new Comment("c1", anchorP,
            "Spectacle is a viewer.",
            "Reword for clarity: open with what the user gets.",
            FixedNow, null);
        var commentH = new Comment("c2", anchorH,
            "## Install",
            "Rename to \"## Quick start\" — \"Install\" implies an installer; this is copy-and-run.",
            FixedNow, null);
        var commentC = new Comment("c3", anchorC,
            "var x = 1;\nvar y = 2;",
            "Use line one assignment style.\nReplace `var y = 2;` with `let y = 2;` if the language allows.",
            FixedNow, null);

        var b1 = new TaggedBlock("b0", "paragraph", 12, "h1", 0, "Spectacle is a viewer.");
        var b2 = new TaggedBlock("b1", "heading", 7, "h2", 0, "## Install");
        var b3 = new TaggedBlock("b2", "code", 30, "h3", 0, "var x = 1;\nvar y = 2;");

        var plan = RevisionPlanExporter.Build(
            @"C:\path\README.md", "abc123", FixedNow,
            new[]
            {
                new MatchedComment(commentP, b1),
                new MatchedComment(commentH, b2),
                new MatchedComment(commentC, b3),
            });

        var expected = System.IO.File.ReadAllText(
            System.IO.Path.Combine("Fixtures", "revision-plan-3-comments.md"));

        // Normalize line endings before comparing — the fixture is LF on disk
        // per .gitattributes; the in-memory plan uses Environment.NewLine.
        var actualNormalized = plan.Replace("\r\n", "\n");
        var expectedNormalized = expected.Replace("\r\n", "\n");

        actualNormalized.Should().Be(expectedNormalized);
    }
}
