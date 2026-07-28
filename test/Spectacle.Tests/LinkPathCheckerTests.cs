using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class LinkPathCheckerTests
{
    [Fact]
    public void Relative_link_that_exists_is_not_flagged()
    {
        LinkPathChecker.Check("See [spec](./other.md).", _ => true).Should().BeEmpty();
    }

    [Fact]
    public void Missing_relative_link_is_flagged_with_its_target()
    {
        var broken = LinkPathChecker.Check("See [spec](./missing.md).", _ => false);

        broken.Should().ContainSingle();
        broken[0].Target.Should().Be("./missing.md");
        broken[0].Reason.Should().Contain("link");
    }

    [Fact]
    public void Missing_relative_image_is_flagged_as_an_image()
    {
        var broken = LinkPathChecker.Check("![diagram](diagrams/flow.png)", _ => false);

        broken.Should().ContainSingle();
        broken[0].Reason.Should().Contain("image");
    }

    [Theory]
    [InlineData("[x](https://example.com/page)")]
    [InlineData("[x](http://example.com)")]
    [InlineData("[x](mailto:a@b.com)")]
    [InlineData("[x](tel:+123)")]
    [InlineData("[x](//cdn.example.com/lib.js)")]
    [InlineData("[x](#a-section)")]
    [InlineData("[x](/site/absolute.md)")]
    public void Non_relative_targets_are_left_alone(string markdown)
    {
        // Predicate returns false for everything; only relative file refs should surface.
        LinkPathChecker.Check(markdown, _ => false).Should().BeEmpty();
    }

    [Fact]
    public void Empty_link_target_is_left_to_the_link_checker()
    {
        LinkPathChecker.Check("[x]()", _ => false).Should().BeEmpty();
    }

    [Fact]
    public void Fragment_is_stripped_before_resolving()
    {
        string? seen = null;
        LinkPathChecker.Check("[x](other.md#a-heading)", target => { seen = target; return true; });

        seen.Should().Be("other.md");
    }

    [Fact]
    public void Percent_encoding_is_decoded_before_resolving()
    {
        string? seen = null;
        LinkPathChecker.Check("[x](my%20file.md)", target => { seen = target; return true; });

        seen.Should().Be("my file.md");
    }

    [Fact]
    public void Pure_fragment_after_a_path_query_is_not_treated_as_a_file()
    {
        // "?query" with no path component leaves nothing to resolve.
        LinkPathChecker.Check("[x](?q=1)", _ => false).Should().BeEmpty();
    }

    [Fact]
    public void Line_number_is_one_based()
    {
        const string content = "# Title\n\nIntro.\n\nSee [x](./gone.md).\n";

        LinkPathChecker.Check(content, _ => false).Should().ContainSingle()
            .Which.Line.Should().Be(5);
    }

    [Fact]
    public void Parent_relative_paths_are_resolved_as_relative()
    {
        string? seen = null;
        LinkPathChecker.Check("[x](../sibling/doc.md)", target => { seen = target; return true; });

        seen.Should().Be("../sibling/doc.md");
    }
}
