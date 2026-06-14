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
}
