using System.Collections.Generic;
using FluentAssertions;
using Spectacle.Gate;
using Xunit;

namespace Spectacle.Tests;

public class GatePolicyTests
{
    private static GatePolicy Policy(params (string Key, string Value)[] grades)
    {
        var map = new Dictionary<string, string>();
        foreach (var (key, value) in grades) map[key] = value;
        return GatePolicy.Create(map, null);
    }

    [Fact]
    public void Default_blocks_on_errors_and_grades_nothing()
    {
        GatePolicy.Default.FailOn.Should().Be(GateSeverity.Error);
        GatePolicy.Default.HasOverrides.Should().BeFalse();
        GatePolicy.Default.SeverityOf("bare-urls", "bare-urls/bare-url").Should().Be(GateSeverity.Error);
    }

    [Fact]
    public void Uses_the_catalogued_default_when_nothing_is_graded()
    {
        GatePolicy.Default.SeverityOf("prose", "prose/hedge").Should().Be(GateSeverity.Info);
        GatePolicy.Default.SeverityOf("fences", "fences/no-language").Should().Be(GateSeverity.Warning);
        GatePolicy.Default.SeverityOf("fences", "fences/unclosed-fence").Should().Be(GateSeverity.Error);
    }

    [Fact]
    public void A_check_level_grade_applies_to_all_of_its_rules()
    {
        var policy = Policy(("toc", "warning"));

        policy.SeverityOf("toc", "toc/stale-toc-entry").Should().Be(GateSeverity.Warning);
        policy.SeverityOf("toc", "toc/missing-from-toc").Should().Be(GateSeverity.Warning);
        policy.SeverityOf("links", "links").Should().Be(GateSeverity.Error);
    }

    [Fact]
    public void A_rule_level_grade_wins_over_its_check()
    {
        var policy = Policy(("toc", "warning"), ("toc/stale-toc-entry", "error"));

        policy.SeverityOf("toc", "toc/stale-toc-entry").Should().Be(GateSeverity.Error);
        policy.SeverityOf("toc", "toc/missing-from-toc").Should().Be(GateSeverity.Warning);
    }

    [Fact]
    public void Grades_are_matched_case_insensitively()
    {
        Policy(("Bare-Urls", "WARNING"))
            .SeverityOf("bare-urls", "bare-urls/bare-url").Should().Be(GateSeverity.Warning);
    }

    [Fact]
    public void Accepts_the_synonyms_other_linters_use()
    {
        GateSeverities.Parse("warn").Should().Be(GateSeverity.Warning);
        GateSeverities.Parse("note").Should().Be(GateSeverity.Info);
        GateSeverities.Parse("off").Should().Be(GateSeverity.Info);
        GateSeverities.Parse(" Error ").Should().Be(GateSeverity.Error);
        GateSeverities.Parse("nonsense").Should().BeNull();
        GateSeverities.Parse(null).Should().BeNull();
    }

    [Fact]
    public void An_unparseable_grade_leaves_the_rule_at_its_default()
    {
        var policy = Policy(("bare-urls", "sometimes"));

        policy.SeverityOf("bare-urls", "bare-urls/bare-url").Should().Be(GateSeverity.Error);
        policy.HasOverrides.Should().BeFalse();
    }

    [Fact]
    public void Blocks_at_or_above_the_threshold()
    {
        var errorGate = GatePolicy.Create(new Dictionary<string, string>(), "error");
        errorGate.Blocks(GateSeverity.Error).Should().BeTrue();
        errorGate.Blocks(GateSeverity.Warning).Should().BeFalse();

        var warningGate = GatePolicy.Create(new Dictionary<string, string>(), "warning");
        warningGate.Blocks(GateSeverity.Error).Should().BeTrue();
        warningGate.Blocks(GateSeverity.Warning).Should().BeTrue();
    }

    [Fact]
    public void Info_never_blocks_even_at_the_lowest_threshold()
    {
        // Advice is advice: the lowest setting reports it without turning it into a build failure.
        var infoGate = GatePolicy.Create(new Dictionary<string, string>(), "info");

        infoGate.Blocks(GateSeverity.Info).Should().BeFalse();
        infoGate.Blocks(GateSeverity.Warning).Should().BeTrue();
    }

    [Fact]
    public void An_unrecognized_threshold_falls_back_to_error()
    {
        GatePolicy.Create(new Dictionary<string, string>(), "whenever").FailOn.Should().Be(GateSeverity.Error);
        GatePolicy.Create(new Dictionary<string, string>(), null).FailOn.Should().Be(GateSeverity.Error);
    }

    [Fact]
    public void WithFailOn_overrides_only_the_threshold()
    {
        var policy = Policy(("toc", "warning")).WithFailOn(GateSeverity.Warning);

        policy.FailOn.Should().Be(GateSeverity.Warning);
        policy.SeverityOf("toc", "toc/stale-toc-entry").Should().Be(GateSeverity.Warning);
    }

    [Fact]
    public void Apply_regrades_a_finding_stream_in_place_order()
    {
        var findings = new[]
        {
            new GateFinding("toc", "toc/stale-toc-entry", GateSeverity.Error, 3, "stale"),
            new GateFinding("links", "links", GateSeverity.Error, 9, "broken"),
        };

        var graded = Policy(("toc", "warning")).Apply(findings);

        graded[0].Severity.Should().Be(GateSeverity.Warning);
        graded[1].Severity.Should().Be(GateSeverity.Error);
        graded.Should().HaveCount(2);
        graded[0].Line.Should().Be(3);
    }

    [Fact]
    public void Reports_unparseable_severity_values_so_a_typo_is_visible()
    {
        var map = new Dictionary<string, string> { ["toc"] = "sometimes", ["links"] = "warning" };

        GatePolicy.UnknownSeverities(map).Should().Equal("toc=sometimes");
    }

    [Fact]
    public void Reports_grades_set_for_ids_that_name_nothing()
    {
        var map = new Dictionary<string, string>
        {
            ["toc"] = "warning",
            ["prose"] = "off",
            ["prose/hedge"] = "off",
            ["not-a-check"] = "warning",
        };

        GatePolicy.UnknownRules(map).Should().Equal("not-a-check");
    }

    [Fact]
    public void OverrideSummary_lists_the_applied_grades_in_id_order()
    {
        Policy(("toc", "warning"), ("bare-urls", "info")).OverrideSummary
            .Should().Equal("bare-urls=info", "toc=warning");
    }
}
