using System;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class BatchReviewTests
{
    private static readonly Func<string, bool> AllExist = _ => true;

    [Fact]
    public void Compute_runs_a_report_per_spec_preserving_order()
    {
        var specs = new (string, string, Func<string, bool>)[]
        {
            ("a.md", "# Clean\n\nProse with no issues.\n", AllExist),
            ("b.md", "# Bad\n\nTODO finish.\n", AllExist),
        };

        var result = BatchReview.Compute(specs);

        result.FileCount.Should().Be(2);
        result.Entries[0].Path.Should().Be("a.md");
        result.Entries[1].Path.Should().Be("b.md");
        result.Entries[0].Report.IssueCount.Should().Be(0);
        result.Entries[1].Report.IssueCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Rollup_counts_files_with_issues_and_total()
    {
        var specs = new (string, string, Func<string, bool>)[]
        {
            ("clean.md", "# Ok\n\nFine.\n", AllExist),
            ("one.md", "# One\n\nTODO a.\n", AllExist),
            ("two.md", "# Two\n\nTODO b.\n\nFIXME c.\n", AllExist),
        };

        var result = BatchReview.Compute(specs);

        result.FileCount.Should().Be(3);
        result.FilesWithIssues.Should().Be(2);
        result.TotalIssues.Should().Be(result.Entries[1].Report.IssueCount + result.Entries[2].Report.IssueCount);
    }

    [Fact]
    public void Compute_on_no_specs_is_empty_rollup()
    {
        var result = BatchReview.Compute(Array.Empty<(string, string, Func<string, bool>)>());

        result.FileCount.Should().Be(0);
        result.FilesWithIssues.Should().Be(0);
        result.TotalIssues.Should().Be(0);
    }

    [Fact]
    public void Per_spec_resolver_validates_relative_targets_independently()
    {
        // Same content, but the first spec's targets all exist and the second's do not.
        const string content = "# Doc\n\nSee [img](./pic.png).\n";
        var specs = new (string, string, Func<string, bool>)[]
        {
            ("found.md", content, _ => true),
            ("missing.md", content, _ => false),
        };

        var result = BatchReview.Compute(specs);

        result.Entries[0].Report.Paths.Should().BeEmpty();
        result.Entries[1].Report.Paths.Should().NotBeEmpty();
    }
}
