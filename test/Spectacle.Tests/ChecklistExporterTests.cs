using System;
using System.Text.Json;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class ChecklistExporterTests
{
    private static readonly ChecklistItem[] Items =
    {
        new(Checked: true, Text: "Returns problem+json", Line: 4),
        new(Checked: false, Text: "Supports OAuth2", Line: 3),
        new(Checked: false, Text: "Rate limiting documented", Line: 7),
    };

    [Fact]
    public void Text_reports_completion_and_lists_open_items()
    {
        var text = ChecklistExporter.Build(Items, @"C:\path\spec.md", json: false);

        text.Should().Contain("spec.md");
        text.Should().Contain("1/3");          // 1 of 3 complete
        text.Should().Contain("Supports OAuth2");
        text.Should().Contain("Rate limiting documented");
    }

    [Fact]
    public void Text_handles_no_items()
    {
        var text = ChecklistExporter.Build(Array.Empty<ChecklistItem>(), @"C:\spec.md", json: false);
        text.Should().Contain("0");
    }

    [Fact]
    public void Json_emits_counts_and_items()
    {
        var json = ChecklistExporter.Build(Items, @"C:\path\spec.md", json: true);

        var root = JsonDocument.Parse(json).RootElement;
        root.GetProperty("source").GetString().Should().Be(@"C:\path\spec.md");
        root.GetProperty("total").GetInt32().Should().Be(3);
        root.GetProperty("done").GetInt32().Should().Be(1);
        root.GetProperty("open").GetInt32().Should().Be(2);
        root.GetProperty("items").GetArrayLength().Should().Be(3);
        root.GetProperty("items")[0].GetProperty("checked").GetBoolean().Should().BeTrue();
        root.GetProperty("items")[0].GetProperty("text").GetString().Should().Be("Returns problem+json");
    }
}
