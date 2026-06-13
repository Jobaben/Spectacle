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
        root.GetProperty("checklist").GetProperty("total").GetInt32().Should().Be(1);
    }
}
