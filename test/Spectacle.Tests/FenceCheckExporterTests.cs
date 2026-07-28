using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class FenceCheckExporterTests
{
    private static readonly IReadOnlyList<FenceIssue> Issues = new[]
    {
        new FenceIssue(3, "no-language", "fenced code block has no language tag"),
        new FenceIssue(9, FenceChecker.UnclosedRule, "code fence opened here is never closed"),
    };

    [Fact]
    public void Text_shows_filename_count_and_each_issue()
    {
        var text = FenceCheckExporter.Build(Issues, @"C:\path\spec.md", json: false);

        text.Should().Contain("spec.md");
        text.Should().Contain("2 issue(s)");
        text.Should().Contain("line 3");
        text.Should().Contain("[no-language]");
        text.Should().Contain("line 9");
        text.Should().Contain("[unclosed-fence]");
    }

    [Fact]
    public void Json_emits_issue_count_and_issues_array()
    {
        var json = FenceCheckExporter.Build(Issues, @"C:\path\spec.md", json: true);

        var root = JsonDocument.Parse(json).RootElement;
        root.GetProperty("source").GetString().Should().Be(@"C:\path\spec.md");
        root.GetProperty("issueCount").GetInt32().Should().Be(2);
        root.GetProperty("issues").GetArrayLength().Should().Be(2);
        root.GetProperty("issues")[0].GetProperty("rule").GetString().Should().Be("no-language");
        root.GetProperty("issues")[0].GetProperty("line").GetInt32().Should().Be(3);
    }

    [Fact]
    public void Text_reports_zero_issues_cleanly()
    {
        FenceCheckExporter.Build(System.Array.Empty<FenceIssue>(), "spec.md", json: false)
            .Should().Contain("0 issue(s)");
    }
}
