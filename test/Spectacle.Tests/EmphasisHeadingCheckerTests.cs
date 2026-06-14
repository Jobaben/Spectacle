using System.Linq;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class EmphasisHeadingCheckerTests
{
    [Fact]
    public void Bold_only_paragraph_is_flagged_as_a_fake_heading()
    {
        const string content = "# Doc\n\n**Overview**\n\nReal prose.\n";

        var finding = EmphasisHeadingChecker.Check(content).Should().ContainSingle().Subject;
        finding.Text.Should().Be("Overview");
        finding.Line.Should().Be(3);
    }

    [Fact]
    public void Italic_only_paragraph_is_flagged()
    {
        const string content = "# Doc\n\n_Goals_\n";

        EmphasisHeadingChecker.Check(content).Should().ContainSingle()
            .Which.Text.Should().Be("Goals");
    }

    [Fact]
    public void A_real_heading_is_not_flagged()
    {
        const string content = "# Doc\n\n## Overview\n\nProse.\n";

        EmphasisHeadingChecker.Check(content).Should().BeEmpty();
    }

    [Fact]
    public void An_emphasized_sentence_ending_in_punctuation_is_not_flagged()
    {
        // Ends in a period — reads as a sentence, not a heading label (markdownlint MD036).
        const string content = "# Doc\n\n**Note this carefully.**\n";

        EmphasisHeadingChecker.Check(content).Should().BeEmpty();
    }

    [Fact]
    public void Emphasis_ending_in_colon_is_not_flagged()
    {
        const string content = "# Doc\n\n**Warning:**\n";

        EmphasisHeadingChecker.Check(content).Should().BeEmpty();
    }

    [Fact]
    public void Emphasis_within_a_larger_paragraph_is_not_flagged()
    {
        const string content = "# Doc\n\nThis is **important** to note.\n";

        EmphasisHeadingChecker.Check(content).Should().BeEmpty();
    }

    [Fact]
    public void A_paragraph_with_two_emphasis_runs_is_not_flagged()
    {
        const string content = "# Doc\n\n**One** **Two**\n";

        EmphasisHeadingChecker.Check(content).Should().BeEmpty();
    }

    [Fact]
    public void An_emphasized_list_item_is_not_flagged()
    {
        // A bolded term in a list is a legitimate construct, not a fake heading.
        const string content = "# Doc\n\n- **Term** \n- **Other**\n";

        EmphasisHeadingChecker.Check(content).Should().BeEmpty();
    }

    [Fact]
    public void An_emphasized_blockquote_line_is_not_flagged()
    {
        const string content = "# Doc\n\n> **Aside**\n";

        EmphasisHeadingChecker.Check(content).Should().BeEmpty();
    }

    [Fact]
    public void Trailing_whitespace_around_the_emphasis_still_flags()
    {
        const string content = "# Doc\n\n**Overview**   \n";

        EmphasisHeadingChecker.Check(content).Should().ContainSingle()
            .Which.Text.Should().Be("Overview");
    }

    [Fact]
    public void Findings_are_ordered_by_line()
    {
        const string content = "**Alpha**\n\n**Beta**\n\n**Gamma**\n";

        EmphasisHeadingChecker.Check(content).Select(f => f.Line)
            .Should().BeInAscendingOrder();
    }

    [Fact]
    public void Empty_document_has_no_findings()
    {
        EmphasisHeadingChecker.Check("").Should().BeEmpty();
        EmphasisHeadingChecker.Check(null).Should().BeEmpty();
    }
}
