using Xunit;
using FluentAssertions;
using Spectacle.Cli;

namespace Spectacle.Tests;

public class CliArgsTests
{
    [Fact]
    public void Empty_args_is_Help() =>
        CliArgs.Parse(Array.Empty<string>()).Should().BeOfType<CliCommand.Help>();

    [Fact]
    public void Dash_help_is_Help() =>
        CliArgs.Parse(new[] { "--help" }).Should().BeOfType<CliCommand.Help>();

    [Fact]
    public void Dash_h_is_Help() =>
        CliArgs.Parse(new[] { "-h" }).Should().BeOfType<CliCommand.Help>();

    [Fact]
    public void Dash_version_is_Version() =>
        CliArgs.Parse(new[] { "--version" }).Should().BeOfType<CliCommand.Version>();

    [Fact]
    public void Dash_register_is_Register() =>
        CliArgs.Parse(new[] { "--register" }).Should().BeOfType<CliCommand.Register>();

    [Fact]
    public void Dash_unregister_is_Unregister() =>
        CliArgs.Parse(new[] { "--unregister" }).Should().BeOfType<CliCommand.Unregister>();

    [Fact]
    public void File_path_is_Open()
    {
        var result = CliArgs.Parse(new[] { @"C:\docs\readme.md" });
        result.Should().BeOfType<CliCommand.Open>()
            .Which.Path.Should().Be(@"C:\docs\readme.md");
    }

    [Fact]
    public void Unknown_flag_is_Help() =>
        CliArgs.Parse(new[] { "--what" }).Should().BeOfType<CliCommand.Help>();

    [Fact]
    public void Multiple_positionals_uses_first()
    {
        var result = CliArgs.Parse(new[] { "a.md", "b.md" });
        result.Should().BeOfType<CliCommand.Open>().Which.Path.Should().Be("a.md");
    }
}
