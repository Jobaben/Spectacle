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

    [Fact]
    public void Lint_after_path_is_Lint()
    {
        var result = CliArgs.Parse(new[] { "doc.md", "--lint" });
        var lint = result.Should().BeOfType<CliCommand.Lint>().Subject;
        lint.Path.Should().Be("doc.md");
        lint.Json.Should().BeFalse();
    }

    [Fact]
    public void Lint_before_path_is_Lint() =>
        CliArgs.Parse(new[] { "--lint", "doc.md" })
            .Should().BeOfType<CliCommand.Lint>().Which.Path.Should().Be("doc.md");

    [Fact]
    public void Lint_json_flag_sets_Json() =>
        CliArgs.Parse(new[] { "doc.md", "--lint", "--json" })
            .Should().BeOfType<CliCommand.Lint>().Which.Json.Should().BeTrue();

    [Fact]
    public void Lint_without_path_is_Help() =>
        CliArgs.Parse(new[] { "--lint" }).Should().BeOfType<CliCommand.Help>();

    [Fact]
    public void Outline_after_path_is_Outline()
    {
        var result = CliArgs.Parse(new[] { "doc.md", "--outline" });
        var outline = result.Should().BeOfType<CliCommand.Outline>().Subject;
        outline.Path.Should().Be("doc.md");
        outline.Json.Should().BeFalse();
    }

    [Fact]
    public void Outline_json_flag_sets_Json() =>
        CliArgs.Parse(new[] { "--outline", "--json", "doc.md" })
            .Should().BeOfType<CliCommand.Outline>().Which.Json.Should().BeTrue();

    [Fact]
    public void Outline_without_path_is_Help() =>
        CliArgs.Parse(new[] { "--outline" }).Should().BeOfType<CliCommand.Help>();

    [Fact]
    public void Checklist_after_path_is_Checklist()
    {
        var result = CliArgs.Parse(new[] { "doc.md", "--checklist" });
        var c = result.Should().BeOfType<CliCommand.Checklist>().Subject;
        c.Path.Should().Be("doc.md");
        c.Json.Should().BeFalse();
    }

    [Fact]
    public void Checklist_json_flag_sets_Json() =>
        CliArgs.Parse(new[] { "doc.md", "--checklist", "--json" })
            .Should().BeOfType<CliCommand.Checklist>().Which.Json.Should().BeTrue();

    [Fact]
    public void Checklist_without_path_is_Help() =>
        CliArgs.Parse(new[] { "--checklist" }).Should().BeOfType<CliCommand.Help>();

    [Fact]
    public void Check_links_after_path_is_CheckLinks()
    {
        var result = CliArgs.Parse(new[] { "doc.md", "--check-links" });
        var c = result.Should().BeOfType<CliCommand.CheckLinks>().Subject;
        c.Path.Should().Be("doc.md");
        c.Json.Should().BeFalse();
    }

    [Fact]
    public void Check_links_json_flag_sets_Json() =>
        CliArgs.Parse(new[] { "--check-links", "--json", "doc.md" })
            .Should().BeOfType<CliCommand.CheckLinks>().Which.Json.Should().BeTrue();

    [Fact]
    public void Check_links_without_path_is_Help() =>
        CliArgs.Parse(new[] { "--check-links" }).Should().BeOfType<CliCommand.Help>();

    [Fact]
    public void Diff_captures_source_and_other_path()
    {
        var result = CliArgs.Parse(new[] { "new.md", "--diff", "old.md" });
        var d = result.Should().BeOfType<CliCommand.Diff>().Subject;
        d.Path.Should().Be("new.md");
        d.OtherPath.Should().Be("old.md");
        d.Json.Should().BeFalse();
    }

    [Fact]
    public void Diff_json_flag_sets_Json()
    {
        var result = CliArgs.Parse(new[] { "new.md", "--diff", "old.md", "--json" });
        var d = result.Should().BeOfType<CliCommand.Diff>().Subject;
        d.OtherPath.Should().Be("old.md");
        d.Json.Should().BeTrue();
    }

    [Fact]
    public void Diff_without_other_path_is_Help() =>
        CliArgs.Parse(new[] { "new.md", "--diff" }).Should().BeOfType<CliCommand.Help>();

    [Fact]
    public void Diff_without_any_path_is_Help() =>
        CliArgs.Parse(new[] { "--diff" }).Should().BeOfType<CliCommand.Help>();

    [Fact]
    public void Check_structure_after_path_is_CheckStructure()
    {
        var result = CliArgs.Parse(new[] { "doc.md", "--check-structure" });
        var c = result.Should().BeOfType<CliCommand.CheckStructure>().Subject;
        c.Path.Should().Be("doc.md");
        c.Json.Should().BeFalse();
    }

    [Fact]
    public void Check_structure_json_flag_sets_Json() =>
        CliArgs.Parse(new[] { "--check-structure", "--json", "doc.md" })
            .Should().BeOfType<CliCommand.CheckStructure>().Which.Json.Should().BeTrue();

    [Fact]
    public void Check_structure_without_path_is_Help() =>
        CliArgs.Parse(new[] { "--check-structure" }).Should().BeOfType<CliCommand.Help>();

    [Fact]
    public void Check_tables_after_path_is_CheckTables()
    {
        var result = CliArgs.Parse(new[] { "doc.md", "--check-tables" });
        var c = result.Should().BeOfType<CliCommand.CheckTables>().Subject;
        c.Path.Should().Be("doc.md");
        c.Json.Should().BeFalse();
    }

    [Fact]
    public void Check_tables_json_flag_sets_Json() =>
        CliArgs.Parse(new[] { "--check-tables", "--json", "doc.md" })
            .Should().BeOfType<CliCommand.CheckTables>().Which.Json.Should().BeTrue();

    [Fact]
    public void Check_tables_without_path_is_Help() =>
        CliArgs.Parse(new[] { "--check-tables" }).Should().BeOfType<CliCommand.Help>();

    [Fact]
    public void Check_fences_after_path_is_CheckFences()
    {
        var result = CliArgs.Parse(new[] { "doc.md", "--check-fences" });
        var c = result.Should().BeOfType<CliCommand.CheckFences>().Subject;
        c.Path.Should().Be("doc.md");
        c.Json.Should().BeFalse();
    }

    [Fact]
    public void Check_fences_json_flag_sets_Json() =>
        CliArgs.Parse(new[] { "--check-fences", "--json", "doc.md" })
            .Should().BeOfType<CliCommand.CheckFences>().Which.Json.Should().BeTrue();

    [Fact]
    public void Check_fences_without_path_is_Help() =>
        CliArgs.Parse(new[] { "--check-fences" }).Should().BeOfType<CliCommand.Help>();

    [Fact]
    public void Check_paths_after_path_is_CheckPaths()
    {
        var result = CliArgs.Parse(new[] { "doc.md", "--check-paths" });
        var c = result.Should().BeOfType<CliCommand.CheckPaths>().Subject;
        c.Path.Should().Be("doc.md");
        c.Json.Should().BeFalse();
    }

    [Fact]
    public void Check_paths_json_flag_sets_Json() =>
        CliArgs.Parse(new[] { "--check-paths", "--json", "doc.md" })
            .Should().BeOfType<CliCommand.CheckPaths>().Which.Json.Should().BeTrue();

    [Fact]
    public void Check_paths_without_path_is_Help() =>
        CliArgs.Parse(new[] { "--check-paths" }).Should().BeOfType<CliCommand.Help>();

    [Fact]
    public void Review_after_path_is_Review()
    {
        var result = CliArgs.Parse(new[] { "doc.md", "--review" });
        var r = result.Should().BeOfType<CliCommand.Review>().Subject;
        r.Path.Should().Be("doc.md");
        r.Json.Should().BeFalse();
    }

    [Fact]
    public void Review_json_flag_sets_Json() =>
        CliArgs.Parse(new[] { "--review", "--json", "doc.md" })
            .Should().BeOfType<CliCommand.Review>().Which.Json.Should().BeTrue();

    [Fact]
    public void Review_without_path_is_Help() =>
        CliArgs.Parse(new[] { "--review" }).Should().BeOfType<CliCommand.Help>();

    [Fact]
    public void Review_without_baseline_has_null_Baseline() =>
        CliArgs.Parse(new[] { "doc.md", "--review" })
            .Should().BeOfType<CliCommand.Review>().Which.Baseline.Should().BeNull();

    [Fact]
    public void Review_with_baseline_captures_second_positional()
    {
        var r = CliArgs.Parse(new[] { "new.md", "--review", "--baseline", "old.md" })
            .Should().BeOfType<CliCommand.Review>().Subject;
        r.Path.Should().Be("new.md");
        r.Baseline.Should().Be("old.md");
    }

    [Fact]
    public void Review_baseline_honours_json_flag()
    {
        var r = CliArgs.Parse(new[] { "new.md", "old.md", "--review", "--baseline", "--json" })
            .Should().BeOfType<CliCommand.Review>().Subject;
        r.Baseline.Should().Be("old.md");
        r.Json.Should().BeTrue();
    }

    [Fact]
    public void Review_baseline_without_second_file_is_Help() =>
        CliArgs.Parse(new[] { "new.md", "--review", "--baseline" }).Should().BeOfType<CliCommand.Help>();
}
