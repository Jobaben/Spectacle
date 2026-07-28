using System.Linq;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class FenceCheckerTests
{
    [Fact]
    public void Closed_fence_with_language_has_no_issues()
    {
        const string content = "# T\n\n```csharp\nvar x = 1;\n```\n";

        FenceChecker.Check(content).Should().BeEmpty();
    }

    [Fact]
    public void Closed_fence_without_language_is_advisory_no_language()
    {
        const string content = "# T\n\n```\nplain text\n```\n";

        FenceChecker.Check(content).Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Line = 3, Rule = "no-language" });
    }

    [Fact]
    public void Unclosed_fence_is_flagged_at_its_opening_line()
    {
        const string content = "# T\n\n```python\ncode that never closes\nmore code\n";

        FenceChecker.Check(content).Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Line = 3, Rule = FenceChecker.UnclosedRule });
    }

    [Fact]
    public void Unclosed_untagged_fence_reports_only_unclosed_not_no_language()
    {
        const string content = "```\nopened but not closed\n";

        var issues = FenceChecker.Check(content);

        issues.Should().ContainSingle().Which.Rule.Should().Be(FenceChecker.UnclosedRule);
    }

    [Fact]
    public void Tilde_fence_is_recognised()
    {
        const string content = "~~~js\ncode\n~~~\n";

        FenceChecker.Check(content).Should().BeEmpty();
    }

    [Fact]
    public void Other_delimiter_inside_a_block_is_content_not_a_toggle()
    {
        // The ``` block stays open across the ~~~ line, then closes with ```.
        const string content = "```\n~~~\nstill inside the code block\n```\n";

        FenceChecker.Check(content).Should().ContainSingle().Which.Rule.Should().Be("no-language");
    }

    [Fact]
    public void Closing_fence_may_be_longer_than_the_opener()
    {
        const string content = "```py\ncode\n`````\n";

        FenceChecker.Check(content).Should().BeEmpty();
    }

    [Fact]
    public void Shorter_run_does_not_close_a_longer_fence()
    {
        // Opener is four backticks; a three-backtick line cannot close it.
        const string content = "````text\ncode\n```\nstill open\n";

        FenceChecker.Check(content).Should().ContainSingle().Which.Rule.Should().Be(FenceChecker.UnclosedRule);
    }

    [Fact]
    public void A_delimiter_with_an_info_string_does_not_close_a_fence()
    {
        // A closing fence carries no info string, so "``` notes" is content — the block
        // is never closed.
        const string content = "```\ncode\n``` notes\n";

        FenceChecker.Check(content).Should().ContainSingle().Which.Rule.Should().Be(FenceChecker.UnclosedRule);
    }

    [Fact]
    public void Prose_without_fences_has_no_issues()
    {
        FenceChecker.Check("# Title\n\nJust prose, no code.\n").Should().BeEmpty();
    }

    [Fact]
    public void Null_input_has_no_issues()
    {
        FenceChecker.Check(null).Should().BeEmpty();
    }
}
