using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class ReviewReportExporterTests
{
    private const string Messy =
        "# Title\n\n### Too Deep\n\nTODO finish.\n\nSee [x](#missing).\n\n" +
        "| A | B |\n|---|---|\n| 1 | 2 | 3 |\n\n- [ ] open item\n";

    [Fact]
    public void Text_groups_findings_by_category_and_shows_total()
    {
        var report = ReviewReport.Compute(Messy);

        var text = ReviewReportExporter.Build(report, @"C:\path\spec.md", json: false);

        text.Should().Contain("spec.md");
        text.Should().Contain("lint");
        text.Should().Contain("structure");
        text.Should().Contain("links");
        text.Should().Contain("tables");
        text.Should().Contain("fences");
        text.Should().Contain("paths");
        text.Should().Contain("duplication");
        text.Should().Contain("alt-text");
        text.Should().Contain("emphasis-headings");
        text.Should().Contain("sections");
        text.Should().Contain("checklist");
    }

    [Fact]
    public void Json_emits_each_category_and_issue_count()
    {
        var report = ReviewReport.Compute(Messy);

        var json = ReviewReportExporter.Build(report, @"C:\path\spec.md", json: true);

        var root = JsonDocument.Parse(json).RootElement;
        root.GetProperty("source").GetString().Should().Be(@"C:\path\spec.md");
        root.GetProperty("issueCount").GetInt32().Should().BeGreaterThan(0);
        root.GetProperty("lint").GetArrayLength().Should().BeGreaterThan(0);
        root.GetProperty("structure").GetArrayLength().Should().BeGreaterThan(0);
        root.GetProperty("links").GetArrayLength().Should().BeGreaterThan(0);
        root.GetProperty("tables").GetArrayLength().Should().BeGreaterThan(0);
        root.GetProperty("fences").ValueKind.Should().Be(JsonValueKind.Array);
        root.GetProperty("paths").ValueKind.Should().Be(JsonValueKind.Array);
        root.GetProperty("duplication").ValueKind.Should().Be(JsonValueKind.Array);
        root.GetProperty("altText").ValueKind.Should().Be(JsonValueKind.Array);
        root.GetProperty("emphasisHeadings").ValueKind.Should().Be(JsonValueKind.Array);
        root.GetProperty("sections").ValueKind.Should().Be(JsonValueKind.Array);
        root.GetProperty("checklist").GetProperty("total").GetInt32().Should().Be(1);
    }

    [Fact]
    public void Json_lists_the_new_check_findings_when_present()
    {
        // Triggers duplication, alt-text, emphasis-heading, and a missing required section.
        const string content =
            "# Title\n\n**Overview**\n\nThe quick brown fox jumps over the lazy dog now.\n\n" +
            "![](d.png)\n\nThe quick brown fox jumps over the lazy dog now.\n";
        var report = ReviewReport.Compute(content, _ => true, new[] { "Acceptance Criteria" });

        var root = JsonDocument.Parse(ReviewReportExporter.Build(report, "spec.md", json: true)).RootElement;

        root.GetProperty("duplication").GetArrayLength().Should().BeGreaterThan(0);
        root.GetProperty("altText").GetArrayLength().Should().BeGreaterThan(0);
        root.GetProperty("emphasisHeadings").GetArrayLength().Should().BeGreaterThan(0);
        root.GetProperty("sections")[0].GetProperty("required").GetString().Should().Be("Acceptance Criteria");
    }

    [Fact]
    public void Markdown_has_a_heading_summary_and_lists_findings()
    {
        var report = ReviewReport.Compute(Messy);

        var md = ReviewReportExporter.Build(report, "spec.md", json: false, markdown: true);

        md.Should().StartWith("# Review: spec.md");
        md.Should().Contain("issue");
        // Categories with findings appear as Markdown subsections; the line numbers are listed.
        md.Should().Contain("## lint");
        md.Should().Contain("## links");
        md.Should().Contain("- line ");
        md.Should().Contain("**Checklist:**");
    }

    [Fact]
    public void Markdown_omits_empty_categories_and_states_clean()
    {
        var md = ReviewReportExporter.Build(
            ReviewReport.Compute("# T\n\nClean.\n"), "spec.md", json: false, markdown: true);

        md.Should().Contain("No issues found.");
        // A clean report should not spam a header for every check that found nothing.
        md.Should().NotContain("## lint");
    }

    [Fact]
    public void Markdown_surfaces_skipped_and_suppressed_in_the_summary()
    {
        const string content =
            "# Title\n\n<!-- spectacle-disable-next-line alt-text -->\n![](d.png)\n";
        var checks = ReviewChecks.Resolve(
            System.Array.Empty<string>(), new[] { "duplication" }, System.Array.Empty<string>());
        var report = ReviewReport.Compute(content, _ => true, System.Array.Empty<string>(), checks);

        var md = ReviewReportExporter.Build(report, "spec.md", json: false, markdown: true);

        md.Should().Contain("suppressed");
        md.Should().Contain("skipped: duplication");
    }

    [Fact]
    public void Review_gate_includes_toc_findings()
    {
        // A "Contents" section whose only entry points at a heading that does not exist.
        const string content =
            "# Spec\n\n## Contents\n\n- [Gone](#gone)\n\n## Overview\n\ntext\n";
        var report = ReviewReport.Compute(content);

        report.TocIssues.Should().NotBeEmpty();
        report.IssueCount.Should().BeGreaterThan(0);

        var root = JsonDocument.Parse(ReviewReportExporter.Build(report, "spec.md", json: true)).RootElement;
        root.GetProperty("toc").GetArrayLength().Should().BeGreaterThan(0);

        ReviewReportExporter.Build(report, "spec.md", json: false).Should().Contain("toc (");
    }

    [Fact]
    public void Toc_check_is_skippable_via_the_gate()
    {
        const string content =
            "# Spec\n\n## Contents\n\n- [Gone](#gone)\n\n## Overview\n\ntext\n";
        var checks = ReviewChecks.Resolve(
            System.Array.Empty<string>(), new[] { "toc" }, System.Array.Empty<string>());
        var report = ReviewReport.Compute(content, _ => true, System.Array.Empty<string>(), checks);

        report.TocIssues.Should().BeEmpty();
        report.Skipped.Should().Contain("toc");
    }

    [Fact]
    public void Clean_verdict_reports_no_skipped_or_suppressed()
    {
        var root = JsonDocument.Parse(
            ReviewReportExporter.Build(ReviewReport.Compute("# T\n\nClean.\n"), "spec.md", json: true)).RootElement;

        root.GetProperty("skippedChecks").GetArrayLength().Should().Be(0);
        root.GetProperty("suppressedCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public void Text_surfaces_skipped_checks_and_suppressed_count()
    {
        // Disable duplication, and suppress the alt-text finding inline.
        const string content =
            "# Title\n\n<!-- spectacle-disable-next-line alt-text -->\n![](d.png)\n";
        var checks = ReviewChecks.Resolve(
            System.Array.Empty<string>(), new[] { "duplication" }, System.Array.Empty<string>());
        var report = ReviewReport.Compute(content, _ => true, System.Array.Empty<string>(), checks);

        var text = ReviewReportExporter.Build(report, "spec.md", json: false);

        text.Should().Contain("1 suppressed");
        text.Should().Contain("skipped: duplication");
    }

    [Fact]
    public void Json_surfaces_skipped_checks_and_suppressed_count()
    {
        const string content =
            "# Title\n\n<!-- spectacle-disable-next-line alt-text -->\n![](d.png)\n";
        var checks = ReviewChecks.Resolve(
            System.Array.Empty<string>(), new[] { "duplication", "paths" }, System.Array.Empty<string>());
        var report = ReviewReport.Compute(content, _ => true, System.Array.Empty<string>(), checks);

        var root = JsonDocument.Parse(ReviewReportExporter.Build(report, "spec.md", json: true)).RootElement;

        root.GetProperty("suppressedCount").GetInt32().Should().Be(1);
        var skipped = root.GetProperty("skippedChecks").EnumerateArray().Select(e => e.GetString()).ToList();
        skipped.Should().Contain("duplication").And.Contain("paths");
    }
}
