using FluentAssertions;
using Spectacle.Cli;
using Xunit;

namespace Spectacle.Tests;

/// <summary>Argument parsing for the gate commands and the standard-input source.</summary>
public class GateCliArgsTests
{
    [Fact]
    public void Parses_the_gate_command()
    {
        var gate = CliArgs.Parse(new[] { "spec.md", "--gate" }).Should().BeOfType<CliCommand.Gate>().Subject;

        gate.Path.Should().Be("spec.md");
        gate.Json.Should().BeFalse();
        gate.FailOn.Should().BeNull();
    }

    [Fact]
    public void Parses_the_gate_output_formats()
    {
        CliArgs.Parse(new[] { "spec.md", "--gate", "--json" }).Should().BeOfType<CliCommand.Gate>()
            .Which.Json.Should().BeTrue();
        CliArgs.Parse(new[] { "spec.md", "--gate", "--md" }).Should().BeOfType<CliCommand.Gate>()
            .Which.Md.Should().BeTrue();
        CliArgs.Parse(new[] { "spec.md", "--gate", "--markdown" }).Should().BeOfType<CliCommand.Gate>()
            .Which.Md.Should().BeTrue();
        CliArgs.Parse(new[] { "spec.md", "--gate", "--sarif" }).Should().BeOfType<CliCommand.Gate>()
            .Which.Sarif.Should().BeTrue();
        CliArgs.Parse(new[] { "spec.md", "--gate", "--github" }).Should().BeOfType<CliCommand.Gate>()
            .Which.Github.Should().BeTrue();
        CliArgs.Parse(new[] { "spec.md", "--gate", "--gh" }).Should().BeOfType<CliCommand.Gate>()
            .Which.Github.Should().BeTrue();
        CliArgs.Parse(new[] { "spec.md", "--gate", "--junit" }).Should().BeOfType<CliCommand.Gate>()
            .Which.Junit.Should().BeTrue();
    }

    [Fact]
    public void Parses_the_gate_threshold_and_selection()
    {
        var gate = CliArgs.Parse(new[]
        {
            "specs", "--gate", "--fail-on=warning", "--skip=toc,paths", "--only=lint", "--config=cfg.json",
        }).Should().BeOfType<CliCommand.Gate>().Subject;

        gate.Path.Should().Be("specs");
        gate.FailOn.Should().Be("warning");
        gate.Skip.Should().Equal("toc", "paths");
        gate.Only.Should().Equal("lint");
        gate.ConfigPath.Should().Be("cfg.json");
    }

    [Fact]
    public void Gate_without_a_source_shows_help()
    {
        CliArgs.Parse(new[] { "--gate" }).Should().BeOfType<CliCommand.Help>();
    }

    [Fact]
    public void Parses_the_fix_brief_command_with_an_optional_output_path()
    {
        var brief = CliArgs.Parse(new[] { "spec.md", "--fix-brief" })
            .Should().BeOfType<CliCommand.FixBrief>().Subject;
        brief.Path.Should().Be("spec.md");
        brief.OutputPath.Should().BeNull();

        var toFile = CliArgs.Parse(new[] { "spec.md", "--fix-brief", "brief.md", "--json" })
            .Should().BeOfType<CliCommand.FixBrief>().Subject;
        toFile.OutputPath.Should().Be("brief.md");
        toFile.Json.Should().BeTrue();
    }

    [Fact]
    public void Accepts_the_brief_alias()
    {
        CliArgs.Parse(new[] { "spec.md", "--brief" }).Should().BeOfType<CliCommand.FixBrief>();
    }

    [Fact]
    public void Parses_the_front_matter_check_with_an_inline_key_template()
    {
        var check = CliArgs.Parse(new[] { "spec.md", "--check-front-matter", "title,status", "--json" })
            .Should().BeOfType<CliCommand.CheckFrontMatter>().Subject;

        check.Path.Should().Be("spec.md");
        check.Required.Should().Be("title,status");
        check.Json.Should().BeTrue();
    }

    [Fact]
    public void The_front_matter_key_template_is_optional_and_can_come_from_a_config()
    {
        var check = CliArgs.Parse(new[] { "spec.md", "--check-front-matter", "--config=cfg.json" })
            .Should().BeOfType<CliCommand.CheckFrontMatter>().Subject;

        check.Required.Should().BeNull();
        check.ConfigPath.Should().Be("cfg.json");
    }

    [Fact]
    public void Accepts_the_metadata_alias()
    {
        CliArgs.Parse(new[] { "spec.md", "--check-metadata" })
            .Should().BeOfType<CliCommand.CheckFrontMatter>();
    }

    [Fact]
    public void Parses_the_generation_artifact_check_and_its_alias()
    {
        CliArgs.Parse(new[] { "spec.md", "--check-ai-artifacts" })
            .Should().BeOfType<CliCommand.CheckAiArtifacts>().Which.Path.Should().Be("spec.md");
        CliArgs.Parse(new[] { "spec.md", "--check-ai", "--json" })
            .Should().BeOfType<CliCommand.CheckAiArtifacts>().Which.Json.Should().BeTrue();
    }

    [Fact]
    public void Review_gains_the_ci_output_formats()
    {
        CliArgs.Parse(new[] { "spec.md", "--review", "--github" })
            .Should().BeOfType<CliCommand.Review>().Which.Github.Should().BeTrue();
        CliArgs.Parse(new[] { "spec.md", "--review", "--junit" })
            .Should().BeOfType<CliCommand.Review>().Which.Junit.Should().BeTrue();
    }

    [Fact]
    public void A_lone_dash_is_the_standard_input_source_not_a_flag()
    {
        // `my-agent write | Spectacle.exe - --gate` has to gate the piped document, which means the
        // positional split must not mistake "-" for an unknown flag.
        CliArgs.Parse(new[] { "-", "--gate" }).Should().BeOfType<CliCommand.Gate>()
            .Which.Path.Should().Be("-");
        CliArgs.Parse(new[] { "-", "--review", "--json" }).Should().BeOfType<CliCommand.Review>()
            .Which.Path.Should().Be("-");
        CliArgs.Parse(new[] { "-", "--check-ai-artifacts" }).Should().BeOfType<CliCommand.CheckAiArtifacts>()
            .Which.Path.Should().Be("-");
    }

    [Fact]
    public void A_flag_is_still_a_flag()
    {
        CliArgs.Parse(new[] { "--help" }).Should().BeOfType<CliCommand.Help>();
        CliArgs.Parse(new[] { "-h" }).Should().BeOfType<CliCommand.Help>();
    }

    [Fact]
    public void The_order_of_the_source_and_the_flag_does_not_matter()
    {
        CliArgs.Parse(new[] { "--gate", "spec.md" }).Should().BeOfType<CliCommand.Gate>()
            .Which.Path.Should().Be("spec.md");
    }
}
