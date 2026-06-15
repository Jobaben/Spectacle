using System.Text.Json;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class HeadingNumberingCheckExporterTests
{
    private static readonly HeadingNumberingIssue[] Sample =
    {
        new(HeadingNumberingChecker.OutOfSequenceRule, 5, "numbered heading 4 breaks the sequence (expected 3)"),
    };

    [Fact]
    public void Text_states_the_count_and_lists_each_issue()
    {
        var text = HeadingNumberingCheckExporter.Build(Sample, "spec.md", json: false);

        text.Should().Contain("spec.md");
        text.Should().Contain("1 heading run");
        text.Should().Contain("line 5");
        text.Should().Contain(HeadingNumberingChecker.OutOfSequenceRule);
        text.Should().Contain("expected 3");
    }

    [Fact]
    public void Text_reports_a_clean_count_when_empty()
    {
        HeadingNumberingCheckExporter.Build(System.Array.Empty<HeadingNumberingIssue>(), "spec.md", json: false)
            .Should().Contain("0 heading run");
    }

    [Fact]
    public void Json_carries_source_count_and_issues()
    {
        var json = HeadingNumberingCheckExporter.Build(Sample, "spec.md", json: true);

        var root = JsonDocument.Parse(json).RootElement;
        root.GetProperty("source").GetString().Should().Be("spec.md");
        root.GetProperty("count").GetInt32().Should().Be(1);
        var issue = root.GetProperty("issues")[0];
        issue.GetProperty("rule").GetString().Should().Be(HeadingNumberingChecker.OutOfSequenceRule);
        issue.GetProperty("line").GetInt32().Should().Be(5);
        issue.GetProperty("message").GetString().Should().Contain("expected 3");
    }
}
