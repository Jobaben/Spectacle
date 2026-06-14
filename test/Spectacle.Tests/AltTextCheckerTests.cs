using System.Linq;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class AltTextCheckerTests
{
    [Fact]
    public void Image_with_alt_text_is_not_flagged()
    {
        const string content = "# Doc\n\n![A wiring diagram](diagram.png)\n";

        AltTextChecker.Check(content).Should().BeEmpty();
    }

    [Fact]
    public void Image_without_alt_text_is_flagged()
    {
        const string content = "# Doc\n\n![](screenshot.png)\n";

        var finding = AltTextChecker.Check(content).Should().ContainSingle().Subject;
        finding.Target.Should().Be("screenshot.png");
        finding.Line.Should().Be(3);
    }

    [Fact]
    public void Whitespace_only_alt_text_is_flagged()
    {
        const string content = "# Doc\n\n![   ](screenshot.png)\n";

        AltTextChecker.Check(content).Should().ContainSingle()
            .Which.Target.Should().Be("screenshot.png");
    }

    [Fact]
    public void Only_images_missing_alt_text_are_reported()
    {
        const string content =
            "![described](a.png)\n\n![](b.png)\n\n![also described](c.png)\n\n![](d.png)\n";

        AltTextChecker.Check(content).Select(i => i.Target)
            .Should().Equal("b.png", "d.png");
    }

    [Fact]
    public void A_link_with_empty_text_is_not_an_image_and_not_flagged()
    {
        // Empty link text on a regular link is LinkChecker's concern, not this check's.
        const string content = "# Doc\n\n[](https://example.com)\n";

        AltTextChecker.Check(content).Should().BeEmpty();
    }

    [Fact]
    public void Formatted_alt_text_counts_as_present()
    {
        const string content = "# Doc\n\n![**bold** caption](diagram.png)\n";

        AltTextChecker.Check(content).Should().BeEmpty();
    }

    [Fact]
    public void Findings_are_ordered_by_line()
    {
        const string content = "![](one.png) and ![](two.png)\n\n![](three.png)\n";

        AltTextChecker.Check(content).Select(i => i.Line)
            .Should().BeInAscendingOrder();
    }
}
