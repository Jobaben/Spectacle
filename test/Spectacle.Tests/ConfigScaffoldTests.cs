using System.IO;
using System.Text.Json;
using FluentAssertions;
using Spectacle.Cli;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class ConfigScaffoldTests
{
    [Fact]
    public void Template_is_valid_json()
    {
        var act = () => JsonDocument.Parse(ConfigScaffold.Template());
        act.Should().NotThrow();
    }

    [Fact]
    public void Template_round_trips_through_the_config_parser()
    {
        var config = SpectacleConfig.Parse(ConfigScaffold.Template());

        config.RequiredSections.Should().Equal("Overview", "Acceptance Criteria", "Non-Goals");
        config.DisabledChecks.Should().BeEmpty();
    }

    [Fact]
    public void Template_note_lists_every_live_check_id()
    {
        var template = ConfigScaffold.Template();

        // The note is sourced from ReviewChecks.All, so it can never advertise a stale set —
        // including a check added after this test was written.
        foreach (var id in ReviewChecks.All)
            template.Should().Contain(id);
    }

    [Fact]
    public void ResolveTargetPath_defaults_to_the_conventional_name_in_cwd()
    {
        ConfigScaffold.ResolveTargetPath(null, _ => false).Should().Be(ConfigScaffold.FileName);
        ConfigScaffold.ResolveTargetPath("  ", _ => false).Should().Be(ConfigScaffold.FileName);
    }

    [Fact]
    public void ResolveTargetPath_places_the_file_inside_a_directory_argument()
    {
        var target = ConfigScaffold.ResolveTargetPath("specs", arg => arg == "specs");

        target.Should().Be(Path.Combine("specs", ConfigScaffold.FileName));
    }

    [Fact]
    public void ResolveTargetPath_takes_a_non_directory_argument_verbatim()
    {
        ConfigScaffold.ResolveTargetPath("custom.json", _ => false).Should().Be("custom.json");
    }
}
