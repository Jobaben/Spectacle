using System.Linq;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

/// <summary>
/// Front matter has to reach the renderer as metadata, not as prose. Left to plain CommonMark, a
/// <c>title: Draft</c> line followed by the closing <c>---</c> is a *setext heading* — so the
/// metadata header silently becomes the document's first h2, landing in the outline, the heading
/// hierarchy, and the table-of-contents check on essentially every document a workflow generates.
/// </summary>
public class FrontMatterRenderingTests
{
    private const string WithHeader = "---\ntitle: Auth design\nstatus: draft\n---\n\n# Auth\n\nText.\n";

    [Fact]
    public void The_header_is_not_rendered_as_a_heading()
    {
        var html = new MdRenderer().ToHtml(WithHeader);

        html.Should().NotContain("<h2");
        html.Should().NotContain("title: Auth design");
        html.Should().Contain("Auth");
    }

    [Fact]
    public void The_header_does_not_appear_in_the_outline()
    {
        var outline = new MdRenderer().Render(WithHeader).Outline;

        outline.Select(e => e.Text).Should().Equal("Auth");
    }

    [Fact]
    public void The_header_does_not_break_the_heading_hierarchy_check()
    {
        // Read as a setext h2 before an h1, the header used to produce a spurious skipped-level or
        // hierarchy finding on every generated document.
        StructureChecker.Check(WithHeader).Should().BeEmpty();
    }

    [Fact]
    public void The_header_is_excluded_from_the_document_statistics()
    {
        var withHeader = DocumentStats.Compute(WithHeader);
        var withoutHeader = DocumentStats.Compute("# Auth\n\nText.\n");

        withHeader.Headings.Should().Be(withoutHeader.Headings);
    }

    [Fact]
    public void The_header_is_not_a_taggable_block()
    {
        // Markdig models the header as a CodeBlock, so it slips into anything that walks blocks by
        // kind. It renders to nothing, so tagging it would mint a block id with no element in the
        // document — a block a comment could anchor to but never be shown on.
        var blocks = new MdRenderer().Render(WithHeader).Blocks;

        blocks.Should().NotContain(b => b.OriginalText.Contains("title: Auth design"));
        blocks.Select(b => b.Kind).Should().NotContain("code");
    }

    [Fact]
    public void The_header_does_not_appear_in_a_block_level_diff()
    {
        // Only the prose changed; the metadata header is not a block, so it cannot register as one.
        var diff = SpecDiff.Compare(WithHeader, "---\ntitle: Auth design\nstatus: final\n---\n\n# Auth\n\nText.\n");

        diff.Added.Should().BeEmpty();
        diff.Removed.Should().BeEmpty();
    }

    [Fact]
    public void A_review_reads_the_body_and_keeps_the_bodys_line_numbers()
    {
        const string content = "---\ntitle: Auth\n---\n\n# Auth\n\nTODO: decide the lifetime.\n";
        var report = ReviewReport.Compute(content);

        report.Lint.Should().HaveCount(1);
        // Line 7 in the real file — stripping blanks the header rather than removing it, so a
        // finding still points at the right line.
        report.Lint[0].Line.Should().Be(7);
    }

    [Fact]
    public void A_review_does_not_read_metadata_values_as_prose()
    {
        // A status of "TBD" is a metadata fact for the front-matter check to judge, not a
        // placeholder marker in the document's prose.
        var report = ReviewReport.Compute("---\nstatus: TBD\n---\n\n# Auth\n\nThe token is signed.\n");

        report.Lint.Should().BeEmpty();
    }

    [Fact]
    public void A_review_still_reports_a_malformed_header()
    {
        var report = ReviewReport.Compute("---\ntitle: Auth\n\n# Auth\n\nText.\n");

        report.FrontMatterIssues.Should().Contain(f => f.Rule == FrontMatterChecker.UnclosedRule);
        report.IssueCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void A_document_without_front_matter_is_unaffected()
    {
        const string plain = "# Auth\n\nText.\n";

        new MdRenderer().ToHtml(plain).Should().Contain("<h1");
        ReviewReport.Compute(plain).FrontMatterIssues.Should().BeEmpty();
        ReviewReport.Compute(plain).IssueCount.Should().Be(0);
    }

    [Fact]
    public void A_thematic_break_further_down_is_still_a_thematic_break()
    {
        var html = new MdRenderer().ToHtml("# Auth\n\nText.\n\n---\n\nMore text.\n");

        html.Should().Contain("<hr");
    }
}
