using System;
using System.Text.Json;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class BatchReviewExporterTests
{
    private static readonly Func<string, bool> AllExist = _ => true;

    private static BatchReviewResult Sample() => BatchReview.Compute(new (string, string, Func<string, bool>)[]
    {
        ("/specs/clean.md", "# Ok\n\nFine prose.\n", AllExist),
        ("/specs/bad.md", "# Bad\n\nTODO finish.\n", AllExist),
    });

    [Fact]
    public void Text_shows_rollup_and_per_file_counts()
    {
        var text = BatchReviewExporter.Build(Sample(), "/specs", json: false);

        text.Should().Contain("2 file(s)");
        text.Should().Contain("1 with issues");
        text.Should().Contain("clean.md");
        text.Should().Contain("bad.md");
    }

    [Fact]
    public void Json_carries_each_files_full_report()
    {
        var json = BatchReviewExporter.Build(Sample(), "/specs", json: true);

        var root = JsonDocument.Parse(json).RootElement;
        root.GetProperty("fileCount").GetInt32().Should().Be(2);
        root.GetProperty("filesWithIssues").GetInt32().Should().Be(1);
        root.GetProperty("totalIssues").GetInt32().Should().BeGreaterThan(0);

        var files = root.GetProperty("files");
        files.GetArrayLength().Should().Be(2);
        files[0].GetProperty("source").GetString().Should().Be("/specs/clean.md");
        files[1].GetProperty("issueCount").GetInt32().Should().BeGreaterThan(0);
        files[1].GetProperty("lint").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public void Json_carries_toc_findings_per_file()
    {
        var result = BatchReview.Compute(new (string, string, Func<string, bool>)[]
        {
            ("/specs/toc.md", "# Spec\n\n## Contents\n\n- [Gone](#gone)\n\n## Overview\n\ntext\n", AllExist),
        });

        var root = JsonDocument.Parse(BatchReviewExporter.Build(result, "/specs", json: true)).RootElement;

        root.GetProperty("files")[0].GetProperty("toc").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public void Markdown_has_a_rollup_header_and_a_section_per_file()
    {
        var md = BatchReviewExporter.Build(Sample(), "/specs", json: false, markdown: true);

        md.Should().StartWith("# Batch review: specs");
        md.Should().Contain("2 file(s)");
        md.Should().Contain("1 with issues");
        md.Should().Contain("clean.md");
        md.Should().Contain("bad.md");
        // The file with issues lists its findings; the clean one says so.
        md.Should().Contain("### lint");
        md.Should().Contain("_No issues._");
    }
}
