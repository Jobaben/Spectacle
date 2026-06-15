using System.Text.Json;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class NumberingCheckExporterTests
{
    private static readonly NumberingIssue[] Sample =
    {
        new(NumberingChecker.OutOfSequenceRule, 3, "ordered list item numbered 4 breaks the sequence (expected 3)"),
    };

    [Fact]
    public void Text_states_the_count_and_lists_each_issue()
    {
        var text = NumberingCheckExporter.Build(Sample, "spec.md", json: false);

        text.Should().Contain("spec.md");
        text.Should().Contain("1 ordered list");
        text.Should().Contain("line 3");
        text.Should().Contain(NumberingChecker.OutOfSequenceRule);
        text.Should().Contain("expected 3");
    }

    [Fact]
    public void Text_reports_a_clean_count_when_empty()
    {
        NumberingCheckExporter.Build(System.Array.Empty<NumberingIssue>(), "spec.md", json: false)
            .Should().Contain("0 ordered list");
    }

    [Fact]
    public void Json_carries_source_count_and_issues()
    {
        var json = NumberingCheckExporter.Build(Sample, "spec.md", json: true);

        var root = JsonDocument.Parse(json).RootElement;
        root.GetProperty("source").GetString().Should().Be("spec.md");
        root.GetProperty("count").GetInt32().Should().Be(1);
        var issue = root.GetProperty("issues")[0];
        issue.GetProperty("rule").GetString().Should().Be(NumberingChecker.OutOfSequenceRule);
        issue.GetProperty("line").GetInt32().Should().Be(3);
        issue.GetProperty("message").GetString().Should().Contain("expected 3");
    }
}
