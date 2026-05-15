using System.Linq;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class BlockTaggerTests
{
    private static RenderResult Render(string md) => new MdRenderer().Render(md);

    [Fact]
    public void Heading_gets_md_block_attributes()
    {
        var r = Render("# Hello\n");

        r.Html.Should().Contain("class=\"md-block\"");
        r.Html.Should().Contain("data-kind=\"heading\"");
        r.Html.Should().Contain("data-block-id=\"b0\"");
        r.Html.Should().Contain("data-line=\"1\"");
        r.Html.Should().Contain("data-occurrence-index=\"0\"");
        r.Html.Should().Contain("tabindex=\"0\"");
        r.Blocks.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                Kind = "heading",
                Line = 1,
                OccurrenceIndex = 0,
                BlockId = "b0"
            });
    }

    [Fact]
    public void Paragraph_and_heading_each_get_one_id()
    {
        var r = Render("# Hi\n\nA paragraph.\n");

        r.Blocks.Select(b => b.Kind).Should().Equal("heading", "paragraph");
        r.Blocks.Select(b => b.BlockId).Should().Equal("b0", "b1");
    }

    [Fact]
    public void Two_identical_paragraphs_get_separate_occurrence_indexes()
    {
        var r = Render("Same.\n\nSame.\n");

        var paras = r.Blocks.Where(b => b.Kind == "paragraph").ToList();
        paras.Should().HaveCount(2);
        paras[0].TextHash.Should().Be(paras[1].TextHash);
        paras[0].OccurrenceIndex.Should().Be(0);
        paras[1].OccurrenceIndex.Should().Be(1);
    }

    [Fact]
    public void Text_hash_is_stable_for_unchanged_input()
    {
        var a = Render("# Hello\n").Blocks[0].TextHash;
        var b = Render("# Hello\n").Blocks[0].TextHash;
        a.Should().Be(b);
    }

    [Fact]
    public void Text_hash_normalizes_line_endings()
    {
        var lf = Render("# Hello\n").Blocks[0].TextHash;
        var crlf = Render("# Hello\r\n").Blocks[0].TextHash;
        crlf.Should().Be(lf);
    }

    [Fact]
    public void Code_block_is_tagged_as_code()
    {
        var r = Render("```cs\nvar x = 1;\n```\n");

        r.Blocks.Should().ContainSingle().Which.Kind.Should().Be("code");
        r.Html.Should().Contain("data-kind=\"code\"");
    }

    [Fact]
    public void Blockquote_is_tagged_once_inner_paragraphs_are_not()
    {
        var r = Render("> a quote\n");

        r.Blocks.Should().ContainSingle().Which.Kind.Should().Be("blockquote");
    }

    [Fact]
    public void List_items_are_tagged_the_list_itself_is_not()
    {
        var r = Render("- one\n- two\n- three\n");

        r.Blocks.Select(b => b.Kind).Should().Equal("list-item", "list-item", "list-item");
        r.Blocks.Select(b => b.OccurrenceIndex).Should().Equal(0, 0, 0);
        r.Blocks.Select(b => b.TextHash).Distinct().Should().HaveCount(3);
    }

    [Fact]
    public void Thematic_break_is_tagged_as_hr()
    {
        var r = Render("---\n");

        r.Blocks.Should().ContainSingle().Which.Kind.Should().Be("hr");
    }

    [Fact]
    public void Original_text_round_trips_normalized()
    {
        var r = Render("Hello world.\n");

        r.Blocks[0].OriginalText.Should().Be("Hello world.");
    }
}
