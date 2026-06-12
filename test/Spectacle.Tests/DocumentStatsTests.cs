using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class DocumentStatsTests
{
    [Fact]
    public void Empty_document_is_all_zero()
    {
        var stats = DocumentStats.Compute("");

        stats.Words.Should().Be(0);
        stats.Characters.Should().Be(0);
        stats.Lines.Should().Be(0);
        stats.Headings.Should().Be(0);
        stats.CodeBlocks.Should().Be(0);
        stats.ReadingTimeMinutes.Should().Be(0);
    }

    [Fact]
    public void Null_document_is_treated_as_empty()
    {
        DocumentStats.Compute(null).Words.Should().Be(0);
    }

    [Fact]
    public void Counts_prose_words_and_headings()
    {
        var stats = DocumentStats.Compute("# Hello world\n\nThis is a test.");

        // "Hello world" (2) + "This is a test." (4)
        stats.Words.Should().Be(6);
        stats.Headings.Should().Be(1);
        stats.ReadingTimeMinutes.Should().Be(1);
    }

    [Fact]
    public void Code_block_bodies_are_counted_as_blocks_not_words()
    {
        var stats = DocumentStats.Compute("```\nvar x = 1;\nvar y = 2;\n```");

        stats.CodeBlocks.Should().Be(1);
        stats.Words.Should().Be(0);
    }

    [Fact]
    public void Links_and_images_are_tallied_separately()
    {
        var stats = DocumentStats.Compute("[Spectacle](https://example.com) and ![logo](logo.png)");

        stats.Links.Should().Be(1);
        stats.Images.Should().Be(1);
    }

    [Fact]
    public void Lines_count_newline_separated_source_lines()
    {
        var stats = DocumentStats.Compute("a\nb\nc");

        stats.Lines.Should().Be(3);
    }

    [Fact]
    public void Reading_time_rounds_up_at_two_hundred_words_per_minute()
    {
        var prose = string.Join(' ', System.Linq.Enumerable.Repeat("word", 250));

        var stats = DocumentStats.Compute(prose);

        stats.Words.Should().Be(250);
        stats.ReadingTimeMinutes.Should().Be(2);
    }

    [Fact]
    public void Short_prose_rounds_up_to_a_one_minute_minimum()
    {
        DocumentStats.Compute("just a few words here").ReadingTimeMinutes.Should().Be(1);
    }
}
