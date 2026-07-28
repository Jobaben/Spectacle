using System.Linq;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class ProseCheckerTests
{
    [Fact]
    public void Clean_prose_yields_no_findings()
    {
        var findings = ProseChecker.Check("The service returns a 200 response with the user record.");

        findings.Should().BeEmpty();
    }

    [Fact]
    public void Null_or_empty_input_is_safe()
    {
        ProseChecker.Check(null).Should().BeEmpty();
        ProseChecker.Check("").Should().BeEmpty();
    }

    [Fact]
    public void Flags_a_hedge_phrase_with_its_rule_and_line()
    {
        var md = "# Title\n\nThe API should probably return JSON.";

        var findings = ProseChecker.Check(md);

        var f = findings.Should().ContainSingle().Subject;
        f.Rule.Should().Be(ProseChecker.HedgeRule);
        f.Phrase.Should().Be("should probably");
        f.Line.Should().Be(3);
        f.Message.Should().Contain("hedging");
    }

    [Fact]
    public void Flags_a_weasel_phrase()
    {
        var findings = ProseChecker.Check("Validate the inputs, persist them, etc.");

        findings.Should().ContainSingle(f => f.Rule == ProseChecker.WeaselRule && f.Phrase == "etc.");
    }

    [Fact]
    public void Flags_a_vague_directive()
    {
        var findings = ProseChecker.Check("Cache the results as appropriate.");

        findings.Should().ContainSingle(f => f.Rule == ProseChecker.VagueDirectiveRule && f.Phrase == "as appropriate");
    }

    [Fact]
    public void Matching_is_case_insensitive()
    {
        var findings = ProseChecker.Check("PERHAPS we retry.");

        findings.Should().ContainSingle(f => f.Phrase == "perhaps");
    }

    [Fact]
    public void Ignores_phrases_inside_fenced_code()
    {
        var md = "Intro paragraph.\n\n```\n# config, etc. as appropriate\nx = 1\n```\n\nDone.";

        ProseChecker.Check(md).Should().BeEmpty();
    }

    [Fact]
    public void Does_not_fire_inside_an_unrelated_word()
    {
        // "etcetera" must not match the "etc." rule; "variously" must not match "various".
        ProseChecker.Check("The etcetera list is variously sorted.").Should().BeEmpty();
    }

    [Fact]
    public void Same_phrase_twice_on_one_line_is_one_finding()
    {
        var findings = ProseChecker.Check("Maybe perhaps perhaps again.");

        findings.Where(f => f.Phrase == "perhaps").Should().HaveCount(1);
    }

    [Fact]
    public void Distinct_phrases_on_one_line_are_separate_findings()
    {
        var findings = ProseChecker.Check("It might want to retry as needed.");

        findings.Should().Contain(f => f.Phrase == "might want to");
        findings.Should().Contain(f => f.Phrase == "as needed");
    }

    [Fact]
    public void Findings_are_ordered_by_line()
    {
        var md = "as needed here.\n\nand so on there.\n\nperhaps last.";

        var lines = ProseChecker.Check(md).Select(f => f.Line).ToList();

        lines.Should().BeInAscendingOrder();
        lines.Should().Equal(1, 3, 5);
    }
}
