using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class HeadingNumberingCheckerTests
{
    [Fact]
    public void Consecutive_numbered_headings_are_clean()
    {
        HeadingNumberingChecker.Check("## 1. Goals\n## 2. Design\n## 3. Rollout\n").Should().BeEmpty();
    }

    [Fact]
    public void A_gap_is_flagged_at_the_first_divergent_heading()
    {
        var issues = HeadingNumberingChecker.Check("# Spec\n\n## 1. Goals\n## 2. Design\n## 4. Rollout\n");

        issues.Should().ContainSingle();
        issues[0].Rule.Should().Be(HeadingNumberingChecker.OutOfSequenceRule);
        issues[0].Line.Should().Be(5);
        issues[0].Message.Should().Contain("4").And.Contain("expected 3");
    }

    [Fact]
    public void All_same_number_is_clean()
    {
        // The lazy `1. 1. 1.` style mirrors the ordered-list rule — not a defect.
        HeadingNumberingChecker.Check("## 1. A\n## 1. B\n## 1. C\n").Should().BeEmpty();
    }

    [Fact]
    public void Consecutive_from_a_non_one_start_is_clean()
    {
        HeadingNumberingChecker.Check("## 3. A\n## 4. B\n## 5. C\n").Should().BeEmpty();
    }

    [Fact]
    public void Paren_delimited_numbering_is_checked_too()
    {
        HeadingNumberingChecker.Check("## 1) A\n## 2) B\n## 3) C\n").Should().BeEmpty();
        HeadingNumberingChecker.Check("## 1) A\n## 2) B\n## 4) D\n").Should().ContainSingle();
    }

    [Fact]
    public void Unnumbered_headings_never_participate()
    {
        HeadingNumberingChecker.Check("# Spec\n## Goals\n## Design\n### Detail\n").Should().BeEmpty();
    }

    [Fact]
    public void Dotted_hierarchical_numbering_is_ignored()
    {
        // "1.2" has a digit, not whitespace, after the first dot — not a flat prefix, so left alone.
        HeadingNumberingChecker.Check("## 1.2 Detail\n## 1.4 Other\n").Should().BeEmpty();
    }

    [Fact]
    public void Each_heading_level_is_a_separate_run()
    {
        // h2 runs 1,2; the h3 under the first is 1,3 (a gap) — only the h3 run is flagged.
        var issues = HeadingNumberingChecker.Check(
            "## 1. Part one\n### 1. a\n### 3. c\n## 2. Part two\n");

        issues.Should().ContainSingle();
        issues[0].Line.Should().Be(3);
        issues[0].Message.Should().Contain("expected 2");
    }

    [Fact]
    public void Sub_numbering_restarting_under_each_parent_is_clean()
    {
        // A shallower heading closes the deeper run, so h3 restarting at 1 under the next h2 is fine.
        HeadingNumberingChecker.Check(
            "## 1. One\n### 1. a\n### 2. b\n## 2. Two\n### 1. a\n### 2. b\n").Should().BeEmpty();
    }

    [Fact]
    public void An_unnumbered_sibling_breaks_the_run_without_flagging()
    {
        // 1, (prose heading), 2 must not be treated as a continuing 1,2 run that bridges the gap.
        HeadingNumberingChecker.Check("## 1. One\n## Notes\n## 2. Two\n").Should().BeEmpty();
    }

    [Fact]
    public void A_single_numbered_heading_has_no_sequence_to_break()
    {
        HeadingNumberingChecker.Check("## 1. Only\n").Should().BeEmpty();
    }

    [Fact]
    public void Numbered_heading_inside_a_code_fence_is_ignored()
    {
        HeadingNumberingChecker.Check("```\n## 1. A\n## 4. D\n```\n").Should().BeEmpty();
    }

    [Fact]
    public void Null_or_empty_input_is_clean()
    {
        HeadingNumberingChecker.Check(null).Should().BeEmpty();
        HeadingNumberingChecker.Check("").Should().BeEmpty();
    }
}
