using System;
using System.Text.Json;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class TableCheckExporterTests
{
    private static readonly TableIssue[] Issues =
    {
        new(3, "row has 3 cells but the header has 2"),
    };

    [Fact]
    public void Text_lists_issues_with_line()
    {
        var text = TableCheckExporter.Build(Issues, @"C:\path\spec.md", json: false);

        text.Should().Contain("spec.md");
        text.Should().Contain("3");
    }

    [Fact]
    public void Text_reports_clean_when_none()
    {
        TableCheckExporter.Build(Array.Empty<TableIssue>(), @"C:\spec.md", json: false)
            .Should().Contain("0");
    }

    [Fact]
    public void Json_emits_structured_issues()
    {
        var json = TableCheckExporter.Build(Issues, @"C:\path\spec.md", json: true);

        var root = JsonDocument.Parse(json).RootElement;
        root.GetProperty("source").GetString().Should().Be(@"C:\path\spec.md");
        root.GetProperty("issueCount").GetInt32().Should().Be(1);
        root.GetProperty("issues")[0].GetProperty("line").GetInt32().Should().Be(3);
    }
}
