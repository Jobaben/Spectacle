using System.Linq;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class DuplicateBlockCheckerTests
{
    [Fact]
    public void No_repeats_yields_no_duplicates()
    {
        const string content =
            "# Spec\n\nThis is the first distinct paragraph.\n\nThis is a second distinct paragraph.\n";

        DuplicateBlockChecker.Check(content).Should().BeEmpty();
    }

    [Fact]
    public void Repeated_paragraph_is_flagged_with_first_occurrence_line()
    {
        const string content =
            "# Spec\n\nThe requirement is stated clearly here.\n\n## Detail\n\nThe requirement is stated clearly here.\n";

        var dup = DuplicateBlockChecker.Check(content).Should().ContainSingle().Subject;
        dup.Kind.Should().Be("paragraph");
        dup.FirstLine.Should().Be(3);
        dup.Line.Should().Be(7);
    }

    [Fact]
    public void Short_repeated_blocks_are_ignored()
    {
        // "N/A" repeats legitimately and is well under the minimum length.
        const string content = "# Spec\n\nN/A\n\n## Section\n\nN/A\n";

        DuplicateBlockChecker.Check(content).Should().BeEmpty();
    }

    [Fact]
    public void Repeat_is_detected_despite_trailing_whitespace_differences()
    {
        const string content =
            "# Spec\n\nAlpha beta gamma delta epsilon.\n\n## Section\n\nAlpha beta gamma delta epsilon.   \n";

        DuplicateBlockChecker.Check(content).Should().ContainSingle()
            .Which.Line.Should().Be(7);
    }

    [Fact]
    public void Three_occurrences_yield_two_duplicates_both_pointing_at_the_first()
    {
        const string content =
            "Repeated boilerplate sentence here.\n\n"
            + "Repeated boilerplate sentence here.\n\n"
            + "Repeated boilerplate sentence here.\n";

        var dups = DuplicateBlockChecker.Check(content);
        dups.Should().HaveCount(2);
        dups.Select(d => d.FirstLine).Should().AllBeEquivalentTo(1);
        dups.Select(d => d.Line).Should().Equal(3, 5);
    }

    [Fact]
    public void Repeated_code_block_is_flagged()
    {
        const string content =
            "```csharp\nvar x = ComputeSomething();\n```\n\nProse between the two code blocks.\n\n"
            + "```csharp\nvar x = ComputeSomething();\n```\n";

        DuplicateBlockChecker.Check(content).Should().ContainSingle()
            .Which.Kind.Should().Be("code");
    }

    [Fact]
    public void Repeated_list_item_is_flagged()
    {
        const string content =
            "- The system shall persist all user edits.\n"
            + "- A different obligation lives on this row.\n"
            + "- The system shall persist all user edits.\n";

        DuplicateBlockChecker.Check(content).Should().ContainSingle()
            .Which.Kind.Should().Be("list-item");
    }

    [Fact]
    public void Thematic_breaks_are_not_treated_as_duplicates()
    {
        const string content =
            "First long enough paragraph of prose.\n\n---\n\nSecond long enough paragraph of prose.\n\n---\n";

        DuplicateBlockChecker.Check(content).Should().BeEmpty();
    }
}
