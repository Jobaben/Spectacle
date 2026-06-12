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

    [Fact]
    public void Stats_after_path_is_Stats()
    {
        CliArgs.Parse(new[] { "doc.md", "--stats" })
            .Should().BeOfType<CliCommand.Stats>().Which.Path.Should().Be("doc.md");
    }

    [Fact]
    public void Stats_before_path_is_Stats()
    {
        CliArgs.Parse(new[] { "--stats", "doc.md" })
            .Should().BeOfType<CliCommand.Stats>().Which.Path.Should().Be("doc.md");
    }

    [Fact]
    public void Stats_without_path_is_Help() =>
        CliArgs.Parse(new[] { "--stats" }).Should().BeOfType<CliCommand.Help>();

    [Fact]
    public void Export_html_with_path_defaults_output_to_null()
    {
        var result = CliArgs.Parse(new[] { "doc.md", "--export-html" });
        var export = result.Should().BeOfType<CliCommand.ExportHtml>().Subject;
        export.Path.Should().Be("doc.md");
        export.OutputPath.Should().BeNull();
    }

    [Fact]
    public void Export_html_captures_output_path()
    {
        var result = CliArgs.Parse(new[] { "doc.md", "--export-html", "out.html" });
        var export = result.Should().BeOfType<CliCommand.ExportHtml>().Subject;
        export.Path.Should().Be("doc.md");
        export.OutputPath.Should().Be("out.html");
    }

    [Fact]
    public void Export_alias_is_ExportHtml() =>
        CliArgs.Parse(new[] { "doc.md", "--export" }).Should().BeOfType<CliCommand.ExportHtml>();

    [Fact]
    public void Export_html_without_path_is_Help() =>
        CliArgs.Parse(new[] { "--export-html" }).Should().BeOfType<CliCommand.Help>();

    [Fact]
    public void Export_html_defaults_to_dark_theme()
    {
        CliArgs.Parse(new[] { "doc.md", "--export-html" })
            .Should().BeOfType<CliCommand.ExportHtml>().Which.Light.Should().BeFalse();
    }

    [Fact]
    public void Export_html_with_light_flag_sets_light()
    {
        CliArgs.Parse(new[] { "doc.md", "--export-html", "--light" })
            .Should().BeOfType<CliCommand.ExportHtml>().Which.Light.Should().BeTrue();
    }

    [Fact]
    public void Export_html_light_flag_does_not_consume_output_path()
    {
        var export = CliArgs.Parse(new[] { "doc.md", "out.html", "--export-html", "--light" })
            .Should().BeOfType<CliCommand.ExportHtml>().Subject;
        export.OutputPath.Should().Be("out.html");
        export.Light.Should().BeTrue();
    }
}
