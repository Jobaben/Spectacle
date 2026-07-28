using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class InlineSuppressionsTests
{
    [Fact]
    public void No_directives_is_empty()
    {
        var sup = InlineSuppressions.Parse("# Title\n\nJust prose.\n");

        sup.IsEmpty.Should().BeTrue();
        sup.IsSuppressed("lint", 1).Should().BeFalse();
    }

    [Fact]
    public void Disable_line_suppresses_the_same_line()
    {
        // Directive on line 2; it suppresses duplication on line 2 only.
        var sup = InlineSuppressions.Parse("# Title\nrepeat <!-- spectacle-disable-line duplication -->\nother\n");

        sup.IsEmpty.Should().BeFalse();
        sup.IsSuppressed("duplication", 2).Should().BeTrue();
        sup.IsSuppressed("duplication", 1).Should().BeFalse();
        sup.IsSuppressed("duplication", 3).Should().BeFalse();
    }

    [Fact]
    public void Disable_next_line_suppresses_the_following_line()
    {
        // Directive on line 2 targets the finding on line 3.
        var sup = InlineSuppressions.Parse("# Title\n<!-- spectacle-disable-next-line alt-text -->\n![](x.png)\n");

        sup.IsSuppressed("alt-text", 3).Should().BeTrue();
        sup.IsSuppressed("alt-text", 2).Should().BeFalse();
    }

    [Fact]
    public void No_ids_suppresses_every_check_on_the_line()
    {
        var sup = InlineSuppressions.Parse("# Title\n<!-- spectacle-disable-next-line -->\nanything\n");

        sup.IsSuppressed("lint", 3).Should().BeTrue();
        sup.IsSuppressed("duplication", 3).Should().BeTrue();
        sup.IsSuppressed("links", 3).Should().BeTrue();
    }

    [Fact]
    public void Multiple_ids_are_all_suppressed_others_are_not()
    {
        var sup = InlineSuppressions.Parse("<!-- spectacle-disable-line duplication, alt-text -->\nx\n");

        sup.IsSuppressed("duplication", 1).Should().BeTrue();
        sup.IsSuppressed("alt-text", 1).Should().BeTrue();
        sup.IsSuppressed("lint", 1).Should().BeFalse();
    }

    [Fact]
    public void Directives_inside_fenced_code_are_ignored()
    {
        const string content =
            "# Title\n\n```\n<!-- spectacle-disable-line duplication -->\n```\n";

        InlineSuppressions.Parse(content).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Ids_are_case_insensitive()
    {
        var sup = InlineSuppressions.Parse("<!-- spectacle-disable-line Duplication -->\nx\n");

        sup.IsSuppressed("duplication", 1).Should().BeTrue();
    }

    [Fact]
    public void A_similar_but_different_keyword_is_not_a_directive()
    {
        // disable-line must not match a longer hyphenated word that merely starts with it.
        InlineSuppressions.Parse("<!-- spectacle-disable-line-foo -->\nx\n").IsEmpty.Should().BeTrue();
    }
}
