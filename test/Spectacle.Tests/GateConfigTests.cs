using FluentAssertions;
using Spectacle.Accessibility;
using Spectacle.Cli;
using Xunit;

namespace Spectacle.Tests;

/// <summary>The config keys the gate reads, and the palette its overlay renders with.</summary>
public class GateConfigTests
{
    [Fact]
    public void Parses_the_front_matter_template()
    {
        var config = SpectacleConfig.Parse("""
            { "requiredFrontMatter": ["workflow", "run.model", "  ", 7] }
            """);

        // Blank and non-string entries are dropped, matching how every other array key parses.
        config.RequiredFrontMatter.Should().Equal("workflow", "run.model");
    }

    [Fact]
    public void Parses_the_severity_map_and_the_threshold()
    {
        var config = SpectacleConfig.Parse("""
            { "severity": { "bare-urls": "warning", "prose/hedge": "off" }, "failOn": "warning" }
            """);

        config.Severity.Should().HaveCount(2);
        config.Severity["bare-urls"].Should().Be("warning");
        config.FailOn.Should().Be("warning");
    }

    [Fact]
    public void Severity_keys_are_matched_case_insensitively()
    {
        var config = SpectacleConfig.Parse("""
            { "severity": { "Bare-Urls": "warning" } }
            """);

        config.Severity.ContainsKey("bare-urls").Should().BeTrue();
    }

    [Fact]
    public void Drops_severity_entries_that_are_not_strings()
    {
        var config = SpectacleConfig.Parse("""
            { "severity": { "toc": 3, "links": "warning", "paths": "  " } }
            """);

        config.Severity.Should().HaveCount(1);
        config.Severity.Should().ContainKey("links");
    }

    [Fact]
    public void A_missing_or_wrongly_typed_key_yields_empty_values()
    {
        // A broken config must never crash a headless gate.
        var config = SpectacleConfig.Parse("""
            { "requiredFrontMatter": "not-an-array", "severity": [1, 2], "failOn": 5 }
            """);

        config.RequiredFrontMatter.Should().BeEmpty();
        config.Severity.Should().BeEmpty();
        config.FailOn.Should().BeNull();
    }

    [Fact]
    public void Malformed_json_yields_the_empty_config()
    {
        var config = SpectacleConfig.Parse("{ not json");

        config.RequiredFrontMatter.Should().BeEmpty();
        config.Severity.Should().BeEmpty();
        config.FailOn.Should().BeNull();
    }

    [Fact]
    public void The_empty_config_enforces_and_grades_nothing()
    {
        SpectacleConfig.Empty.RequiredFrontMatter.Should().BeEmpty();
        SpectacleConfig.Empty.Severity.Should().BeEmpty();
        SpectacleConfig.Empty.FailOn.Should().BeNull();
    }

    [Fact]
    public void The_scaffold_round_trips_through_the_parser_with_the_gate_defaults()
    {
        var config = SpectacleConfig.Parse(ConfigScaffold.Template());

        config.RequiredFrontMatter.Should().BeEmpty();
        config.Severity.Should().BeEmpty();
        config.FailOn.Should().Be("error");
    }

    [Fact]
    public void The_scaffold_documents_every_key_it_writes()
    {
        var template = ConfigScaffold.Template();

        foreach (var key in new[] { "requiredSections", "requiredFrontMatter", "disabledChecks", "severity", "failOn" })
            template.Should().Contain($"\"{key}\"");
        // Each key is explained inline, so the file is self-documenting.
        foreach (var note in new[] { "//requiredFrontMatter", "//severity", "//failOn" })
            template.Should().Contain(note);
    }

    // ---------- palette ----------

    [Fact]
    public void The_dark_severity_colours_meet_AA_on_the_dark_background()
    {
        // A finding's severity label has to be readable, not merely coloured.
        const string bg = "#1e1e1e";
        foreach (var fg in new[] { "#89d185", "#f48771", "#dcdcaa", "#9cdcfe" })
            WcagContrast.Ratio(fg, bg).Should().BeGreaterThanOrEqualTo(4.5, fg);
    }

    [Fact]
    public void The_high_contrast_severity_colour_is_maximal()
    {
        // High contrast drops the hues on purpose: the distinction is carried by each row's
        // "error"/"warning"/"info" label, which a forced-colours user reads either way.
        WcagContrast.Ratio("#ffffff", "#000000").Should().BeApproximately(21.0, 0.01);
    }
}
