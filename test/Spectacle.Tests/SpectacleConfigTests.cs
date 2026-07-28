using FluentAssertions;
using Spectacle.Cli;
using Xunit;

namespace Spectacle.Tests;

public class SpectacleConfigTests
{
    [Fact]
    public void Parses_required_sections_array()
    {
        var config = SpectacleConfig.Parse("""{ "requiredSections": ["Overview", "Acceptance Criteria"] }""");

        config.RequiredSections.Should().Equal("Overview", "Acceptance Criteria");
    }

    [Fact]
    public void Blank_and_whitespace_entries_are_dropped()
    {
        var config = SpectacleConfig.Parse("""{ "requiredSections": ["Overview", "", "   "] }""");

        config.RequiredSections.Should().Equal("Overview");
    }

    [Fact]
    public void Non_string_entries_are_ignored()
    {
        var config = SpectacleConfig.Parse("""{ "requiredSections": ["Overview", 42, true, null] }""");

        config.RequiredSections.Should().Equal("Overview");
    }

    [Fact]
    public void Missing_key_yields_empty_config()
    {
        SpectacleConfig.Parse("""{ "other": 1 }""").RequiredSections.Should().BeEmpty();
    }

    [Fact]
    public void Non_array_value_yields_empty_config()
    {
        SpectacleConfig.Parse("""{ "requiredSections": "Overview" }""").RequiredSections.Should().BeEmpty();
    }

    [Fact]
    public void Malformed_json_yields_empty_config_without_throwing()
    {
        SpectacleConfig.Parse("{ not json").RequiredSections.Should().BeEmpty();
    }

    [Fact]
    public void Empty_or_null_input_yields_empty_config()
    {
        SpectacleConfig.Parse(null).RequiredSections.Should().BeEmpty();
        SpectacleConfig.Parse("").RequiredSections.Should().BeEmpty();
        SpectacleConfig.Parse("   ").RequiredSections.Should().BeEmpty();
    }

    [Fact]
    public void Non_object_root_yields_empty_config()
    {
        SpectacleConfig.Parse("[1, 2, 3]").RequiredSections.Should().BeEmpty();
    }

    [Fact]
    public void Parses_disabled_checks_array()
    {
        var config = SpectacleConfig.Parse("""{ "disabledChecks": ["duplication", "alt-text"] }""");

        config.DisabledChecks.Should().Equal("duplication", "alt-text");
    }

    [Fact]
    public void Required_sections_and_disabled_checks_parse_together()
    {
        var config = SpectacleConfig.Parse(
            """{ "requiredSections": ["Overview"], "disabledChecks": ["paths"] }""");

        config.RequiredSections.Should().Equal("Overview");
        config.DisabledChecks.Should().Equal("paths");
    }

    [Fact]
    public void Missing_disabled_checks_yields_empty_list()
    {
        SpectacleConfig.Parse("""{ "requiredSections": ["Overview"] }""")
            .DisabledChecks.Should().BeEmpty();
    }

    [Fact]
    public void Non_array_disabled_checks_yields_empty_list()
    {
        SpectacleConfig.Parse("""{ "disabledChecks": "duplication" }""")
            .DisabledChecks.Should().BeEmpty();
    }

    [Fact]
    public void Empty_config_has_both_lists_empty()
    {
        SpectacleConfig.Empty.RequiredSections.Should().BeEmpty();
        SpectacleConfig.Empty.DisabledChecks.Should().BeEmpty();
    }
}
