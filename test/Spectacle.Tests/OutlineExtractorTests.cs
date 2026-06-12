using System.Linq;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class OutlineExtractorTests
{
    private static RenderResult Render(string md) => new MdRenderer().Render(md);

    [Fact]
    public void Extracts_headings_in_document_order_with_levels()
    {
        var r = Render("# One\n\n## Two\n\n### Three\n\nbody\n\n## Four\n");

        r.Outline.Select(e => e.Text).Should().Equal("One", "Two", "Three", "Four");
        r.Outline.Select(e => e.Level).Should().Equal(1, 2, 3, 2);
    }

    [Fact]
    public void Ignores_non_heading_content()
    {
        var r = Render("A paragraph.\n\n- a list item\n\n> a quote\n");

        r.Outline.Should().BeEmpty();
    }

    [Fact]
    public void Id_matches_the_auto_generated_heading_anchor()
    {
        var r = Render("# Hello World\n");

        r.Outline.Should().ContainSingle();
        r.Outline[0].Id.Should().Be("hello-world");
        // The same slug must be rendered onto the heading so the preview can
        // scroll to it via document.getElementById.
        r.Html.Should().Contain("id=\"hello-world\"");
    }

    [Fact]
    public void Line_is_one_based_and_matches_block_tagging()
    {
        var r = Render("\n\n# Heading\n");

        r.Outline.Should().ContainSingle().Which.Line.Should().Be(3);
    }

    [Fact]
    public void Flattens_inline_markup_to_plain_text()
    {
        var r = Render("# A *bold* and `code` title\n");

        r.Outline.Should().ContainSingle()
            .Which.Text.Should().Be("A bold and code title");
    }

    [Fact]
    public void Link_text_contributes_heading_text()
    {
        var r = Render("# See [the docs](https://example.com)\n");

        r.Outline.Should().ContainSingle().Which.Text.Should().Be("See the docs");
    }

    [Fact]
    public void Duplicate_heading_text_gets_distinct_anchors()
    {
        // Markdig's auto-identifier disambiguates repeats with a numeric suffix;
        // the outline must carry both ids so each entry jumps to its own heading.
        var r = Render("# Notes\n\n## Notes\n");

        r.Outline.Select(e => e.Id).Should().Equal("notes", "notes-1");
    }

    [Fact]
    public void Empty_document_yields_empty_outline()
    {
        Render(string.Empty).Outline.Should().BeEmpty();
    }
}
