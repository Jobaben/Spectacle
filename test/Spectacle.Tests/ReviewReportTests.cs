using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class ReviewReportTests
{
    [Fact]
    public void Clean_spec_reports_zero_issues()
    {
        const string content =
            "# Title\n\n## Section\n\nComplete prose with a working [link](#section).\n";

        var report = ReviewReport.Compute(content);

        report.IssueCount.Should().Be(0);
        report.Lint.Should().BeEmpty();
        report.Structure.Should().BeEmpty();
        report.Links.Should().BeEmpty();
        report.Tables.Should().BeEmpty();
    }

    [Fact]
    public void Aggregates_findings_from_every_category()
    {
        const string content =
            // placeholder (lint) + skipped level (structure) + broken link + bad table row
            "# Title\n\n### Too Deep\n\nTODO finish this.\n\nSee [x](#missing).\n\n" +
            "| A | B |\n|---|---|\n| 1 | 2 | 3 |\n";

        var report = ReviewReport.Compute(content);

        report.Lint.Should().NotBeEmpty();
        report.Structure.Should().Contain(f => f.Rule == "skipped-level");
        report.Links.Should().NotBeEmpty();
        report.Tables.Should().NotBeEmpty();
        report.IssueCount.Should()
            .Be(report.Lint.Count + report.Structure.Count + report.Links.Count + report.Tables.Count);
    }

    [Fact]
    public void Counts_checklist_completion()
    {
        const string content = "# Title\n\n- [x] done\n- [ ] open\n- [ ] also open\n";

        var report = ReviewReport.Compute(content);

        report.ChecklistTotal.Should().Be(3);
        report.ChecklistDone.Should().Be(1);
    }
}
