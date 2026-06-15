using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class NumberingCheckerTests
{
    [Fact]
    public void Consecutive_from_one_is_clean()
    {
        NumberingChecker.Check("1. a\n2. b\n3. c\n").Should().BeEmpty();
    }

    [Fact]
    public void All_same_marker_is_clean()
    {
        // The lazy `1. 1. 1.` style renders sequentially everywhere — not a defect.
        NumberingChecker.Check("1. a\n1. b\n1. c\n").Should().BeEmpty();
    }

    [Fact]
    public void Consecutive_from_a_non_one_start_is_clean()
    {
        NumberingChecker.Check("3. a\n4. b\n5. c\n").Should().BeEmpty();
    }

    [Fact]
    public void Consecutive_from_zero_is_clean()
    {
        NumberingChecker.Check("0. a\n1. b\n2. c\n").Should().BeEmpty();
    }

    [Fact]
    public void A_gap_is_flagged_at_the_first_divergent_item()
    {
        var issues = NumberingChecker.Check("1. a\n2. b\n4. d\n");

        issues.Should().ContainSingle();
        issues[0].Rule.Should().Be(NumberingChecker.OutOfSequenceRule);
        issues[0].Line.Should().Be(3);
        issues[0].Message.Should().Contain("4").And.Contain("expected 3");
    }

    [Fact]
    public void A_duplicate_number_is_flagged()
    {
        var issues = NumberingChecker.Check("1. a\n2. b\n2. c\n");

        issues.Should().ContainSingle();
        issues[0].Line.Should().Be(3);
        issues[0].Message.Should().Contain("expected 3");
    }

    [Fact]
    public void An_out_of_order_number_is_flagged()
    {
        var issues = NumberingChecker.Check("1. a\n3. b\n2. c\n");

        issues.Should().ContainSingle();
        issues[0].Line.Should().Be(2);
        issues[0].Message.Should().Contain("expected 2");
    }

    [Fact]
    public void Paren_delimited_ordered_lists_are_checked_too()
    {
        NumberingChecker.Check("1) a\n2) b\n3) c\n").Should().BeEmpty();
        NumberingChecker.Check("1) a\n2) b\n4) d\n").Should().ContainSingle();
    }

    [Fact]
    public void Single_item_list_has_no_sequence_to_break()
    {
        NumberingChecker.Check("1. only\n").Should().BeEmpty();
    }

    [Fact]
    public void Unordered_lists_are_ignored()
    {
        NumberingChecker.Check("- a\n- b\n- c\n").Should().BeEmpty();
    }

    [Fact]
    public void Fake_ordered_list_inside_a_code_fence_is_ignored()
    {
        NumberingChecker.Check("```\n1. a\n3. c\n```\n").Should().BeEmpty();
    }

    [Fact]
    public void Each_list_is_judged_independently()
    {
        // A clean list, a blank-line break, then a broken one: only the broken list is flagged.
        var issues = NumberingChecker.Check("1. a\n2. b\n\nSome prose.\n\n1. x\n2. y\n4. z\n");

        issues.Should().ContainSingle();
        issues[0].Message.Should().Contain("expected 3");
    }

    [Fact]
    public void Nested_ordered_lists_are_checked_on_their_own()
    {
        // Outer list is consecutive; the nested list skips from 1 to 3.
        var issues = NumberingChecker.Check("1. parent\n   1. child\n   3. child3\n2. parent2\n");

        issues.Should().ContainSingle();
        issues[0].Line.Should().Be(3);
        issues[0].Message.Should().Contain("expected 2");
    }

    [Fact]
    public void Null_or_empty_input_is_clean()
    {
        NumberingChecker.Check(null).Should().BeEmpty();
        NumberingChecker.Check("").Should().BeEmpty();
    }
}
