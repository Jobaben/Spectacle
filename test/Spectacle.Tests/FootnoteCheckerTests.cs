using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class FootnoteCheckerTests
{
    [Fact]
    public void Undefined_footnote_reference_is_flagged()
    {
        var footnotes = FootnoteChecker.Check("A bold claim.[^src]\n");

        footnotes.Should().ContainSingle();
        footnotes[0].Line.Should().Be(1);
        footnotes[0].Label.Should().Be("src");
    }

    [Fact]
    public void Defined_footnote_reference_is_left_alone()
    {
        FootnoteChecker.Check("A bold claim.[^src]\n\n[^src]: The source of the claim.\n").Should().BeEmpty();
    }

    [Fact]
    public void Definition_marker_is_not_treated_as_a_reference()
    {
        // A lone definition with no reference is not an undefined *reference*.
        FootnoteChecker.Check("[^orphan]: An unreferenced footnote.\n").Should().BeEmpty();
    }

    [Fact]
    public void Footnote_in_a_code_span_is_ignored()
    {
        FootnoteChecker.Check("Markdown footnotes use `[^id]` syntax.\n").Should().BeEmpty();
    }

    [Fact]
    public void Footnote_in_a_fenced_code_block_is_ignored()
    {
        FootnoteChecker.Check("```\nsee [^missing]\n```\n").Should().BeEmpty();
    }

    [Fact]
    public void Label_matching_is_case_insensitive()
    {
        FootnoteChecker.Check("A claim.[^Source]\n\n[^source]: defined.\n").Should().BeEmpty();
    }

    [Fact]
    public void Each_undefined_reference_occurrence_is_reported()
    {
        var footnotes = FootnoteChecker.Check("First.[^a]\n\nSecond.[^a]\n");

        footnotes.Should().HaveCount(2);
        footnotes.Should().OnlyContain(f => f.Label == "a");
    }

    [Fact]
    public void Empty_input_yields_no_findings()
    {
        FootnoteChecker.Check("").Should().BeEmpty();
        FootnoteChecker.Check(null).Should().BeEmpty();
    }
}
