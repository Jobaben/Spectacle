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
    public void Revision_plan_after_path_is_RevisionPlan()
    {
        var result = CliArgs.Parse(new[] { "doc.md", "--revision-plan" });
        var plan = result.Should().BeOfType<CliCommand.RevisionPlan>().Subject;
        plan.Path.Should().Be("doc.md");
        plan.OutputPath.Should().BeNull();
        plan.Json.Should().BeFalse();
    }

    [Fact]
    public void Revision_plan_before_path_is_RevisionPlan()
    {
        CliArgs.Parse(new[] { "--revision-plan", "doc.md" })
            .Should().BeOfType<CliCommand.RevisionPlan>().Which.Path.Should().Be("doc.md");
    }

    [Fact]
    public void Revisions_alias_is_RevisionPlan() =>
        CliArgs.Parse(new[] { "doc.md", "--revisions" }).Should().BeOfType<CliCommand.RevisionPlan>();

    [Fact]
    public void Revision_plan_captures_output_path()
    {
        var result = CliArgs.Parse(new[] { "doc.md", "--revision-plan", "out.md" });
        var plan = result.Should().BeOfType<CliCommand.RevisionPlan>().Subject;
        plan.Path.Should().Be("doc.md");
        plan.OutputPath.Should().Be("out.md");
    }

    [Fact]
    public void Revision_plan_json_flag_sets_Json()
    {
        var result = CliArgs.Parse(new[] { "--revision-plan", "--json", "doc.md" });
        var plan = result.Should().BeOfType<CliCommand.RevisionPlan>().Subject;
        plan.Path.Should().Be("doc.md");
        plan.Json.Should().BeTrue();
    }

    [Fact]
    public void Revision_plan_json_with_output_path()
    {
        var result = CliArgs.Parse(new[] { "doc.md", "--revision-plan", "--json", "out.json" });
        var plan = result.Should().BeOfType<CliCommand.RevisionPlan>().Subject;
        plan.OutputPath.Should().Be("out.json");
        plan.Json.Should().BeTrue();
    }

    [Fact]
    public void Revision_plan_without_path_is_Help() =>
        CliArgs.Parse(new[] { "--revision-plan" }).Should().BeOfType<CliCommand.Help>();

    [Fact]
    public void Json_without_revision_plan_opens_file() =>
        CliArgs.Parse(new[] { "doc.md", "--json" }).Should().BeOfType<CliCommand.Open>();

    [Fact]
    public void Revision_plan_unresolved_flag_sets_UnresolvedOnly()
    {
        var result = CliArgs.Parse(new[] { "doc.md", "--revision-plan", "--unresolved" });
        var plan = result.Should().BeOfType<CliCommand.RevisionPlan>().Subject;
        plan.UnresolvedOnly.Should().BeTrue();
    }

    [Fact]
    public void Revision_plan_defaults_UnresolvedOnly_false()
    {
        var result = CliArgs.Parse(new[] { "doc.md", "--revision-plan" });
        result.Should().BeOfType<CliCommand.RevisionPlan>().Which.UnresolvedOnly.Should().BeFalse();
    }

    [Fact]
    public void Review_summary_after_path_is_ReviewSummary()
    {
        var result = CliArgs.Parse(new[] { "doc.md", "--review-summary" });
        var s = result.Should().BeOfType<CliCommand.ReviewSummary>().Subject;
        s.Path.Should().Be("doc.md");
        s.Json.Should().BeFalse();
    }

    [Fact]
    public void Review_summary_before_path_is_ReviewSummary() =>
        CliArgs.Parse(new[] { "--review-summary", "doc.md" })
            .Should().BeOfType<CliCommand.ReviewSummary>().Which.Path.Should().Be("doc.md");

    [Fact]
    public void Review_summary_json_flag_sets_Json()
    {
        var result = CliArgs.Parse(new[] { "doc.md", "--review-summary", "--json" });
        result.Should().BeOfType<CliCommand.ReviewSummary>().Which.Json.Should().BeTrue();
    }

    [Fact]
    public void Review_summary_without_path_is_Help() =>
        CliArgs.Parse(new[] { "--review-summary" }).Should().BeOfType<CliCommand.Help>();
}
