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
        report.Fences.Should().BeEmpty();
        report.Paths.Should().BeEmpty();
        report.Duplication.Should().BeEmpty();
        report.AltText.Should().BeEmpty();
        report.EmphasisHeadings.Should().BeEmpty();
        report.Sections.Should().BeEmpty();
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
            .Be(report.Lint.Count + report.Structure.Count + report.Links.Count + report.Tables.Count
                + report.Fences.Count + report.Paths.Count + report.Duplication.Count
                + report.AltText.Count + report.EmphasisHeadings.Count + report.Sections.Count);
    }

    [Fact]
    public void Verdict_includes_duplication_alt_text_and_emphasis_headings()
    {
        // A verbatim-repeated paragraph (duplication), an image with no alt text, and an
        // emphasized line used as a heading — all now part of the one-shot verdict.
        const string content =
            "# Title\n\n**Overview**\n\nThe quick brown fox jumps over the lazy dog today.\n\n" +
            "![](diagram.png)\n\nThe quick brown fox jumps over the lazy dog today.\n";

        var report = ReviewReport.Compute(content);

        report.Duplication.Should().NotBeEmpty();
        report.AltText.Should().NotBeEmpty();
        report.EmphasisHeadings.Should().Contain(e => e.Text == "Overview");
        report.IssueCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Enforces_required_sections_when_a_template_is_given()
    {
        const string content = "# Spec\n\n## Overview\n\nText.\n";

        var report = ReviewReport.Compute(
            content, _ => true, new[] { "Overview", "Acceptance Criteria" });

        report.Sections.Should().ContainSingle(s => s.Required == "Acceptance Criteria");
        report.IssueCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void No_template_leaves_the_sections_check_a_noop()
    {
        const string content = "# Spec\n\nNo headings beyond the title.\n";

        ReviewReport.Compute(content).Sections.Should().BeEmpty();
        ReviewReport.Compute(content, _ => true).Sections.Should().BeEmpty();
    }

    [Fact]
    public void Unclosed_fence_counts_as_an_issue_but_a_missing_language_tag_does_not()
    {
        // A closed-but-untagged block is advisory only; the aggregate verdict ignores it.
        ReviewReport.Compute("# T\n\n```\nplain\n```\n").Fences.Should().BeEmpty();

        var report = ReviewReport.Compute("# T\n\n```python\nnever closed\n");
        report.Fences.Should().ContainSingle().Which.Rule.Should().Be(FenceChecker.UnclosedRule);
        report.IssueCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Missing_relative_path_counts_as_an_issue_when_resolved_against_disk()
    {
        const string content = "# T\n\nSee [other](./missing.md).\n";

        ReviewReport.Compute(content, _ => true).Paths.Should().BeEmpty();
        ReviewReport.Compute(content, _ => false).Paths.Should().ContainSingle();
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
