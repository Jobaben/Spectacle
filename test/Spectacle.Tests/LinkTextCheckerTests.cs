using System.Linq;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class LinkTextCheckerTests
{
    [Fact]
    public void Descriptive_link_text_is_not_flagged()
    {
        const string content = "# Doc\n\nSee the [deployment runbook](runbook.md) for details.\n";

        LinkTextChecker.Check(content).Should().BeEmpty();
    }

    [Theory]
    [InlineData("click here")]
    [InlineData("here")]
    [InlineData("link")]
    [InlineData("more")]
    [InlineData("read more")]
    [InlineData("this")]
    public void Non_descriptive_link_text_is_flagged(string text)
    {
        var content = $"# Doc\n\nRead the guide [{text}](guide.md).\n";

        var finding = LinkTextChecker.Check(content).Should().ContainSingle().Subject;
        finding.Line.Should().Be(3);
        finding.Reason.Should().Contain("non-descriptive");
        LinkTextChecker.RuleOf(finding).Should().Be(LinkTextChecker.NonDescriptiveRule);
    }

    [Fact]
    public void Matching_is_case_insensitive_and_tolerates_trailing_punctuation()
    {
        const string content = "# Doc\n\nGo [Click Here!](x.md) or [HERE](y.md).\n";

        LinkTextChecker.Check(content).Should().HaveCount(2);
    }

    [Fact]
    public void Empty_link_text_is_flagged_as_empty()
    {
        // Distinct from LinkChecker's empty-target rule: here the target is fine, the text is blank.
        const string content = "# Doc\n\n[](https://example.com)\n";

        var finding = LinkTextChecker.Check(content).Should().ContainSingle().Subject;
        finding.Reason.Should().Contain("empty");
        LinkTextChecker.RuleOf(finding).Should().Be(LinkTextChecker.EmptyRule);
    }

    [Fact]
    public void Images_are_not_link_text_findings()
    {
        // An image's text is alt text — AltTextChecker's concern, not this check's.
        const string content = "# Doc\n\n![](diagram.png)\n";

        LinkTextChecker.Check(content).Should().BeEmpty();
    }

    [Fact]
    public void A_word_containing_a_flagged_word_is_not_flagged()
    {
        // "linker" / "therein" must not match "link" / "here" — the comparison is whole-text, not substring.
        const string content = "# Doc\n\nThe [linker documentation](linker.md) and [the rationale therein](why.md).\n";

        LinkTextChecker.Check(content).Should().BeEmpty();
    }

    [Fact]
    public void Formatted_non_descriptive_text_still_counts()
    {
        // Emphasis around the text must not hide it: **here** is still "here".
        const string content = "# Doc\n\nSee [**here**](x.md).\n";

        LinkTextChecker.Check(content).Should().ContainSingle();
    }

    [Fact]
    public void Findings_are_ordered_by_line()
    {
        const string content = "[here](a.md)\n\n[click here](b.md)\n\n[more](c.md)\n";

        LinkTextChecker.Check(content).Select(f => f.Line).Should().BeInAscendingOrder();
    }
}
