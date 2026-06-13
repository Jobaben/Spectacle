using System;
using System.Text.Json;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class StructureCheckExporterTests
{
    private static readonly StructureFinding[] Findings =
    {
        new("skipped-level", 3, "heading jumps from level 1 to level 3"),
        new("duplicate-heading", 7, "duplicate heading 'Setup'"),
    };

    [Fact]
    public void Text_lists_findings_with_line_and_rule()
    {
        var text = StructureCheckExporter.Build(Findings, @"C:\path\spec.md", json: false);

        text.Should().Contain("spec.md");
        text.Should().Contain("3");
        text.Should().Contain("skipped-level");
        text.Should().Contain("duplicate-heading");
    }

    [Fact]
    public void Text_reports_clean_when_none()
    {
        StructureCheckExporter.Build(Array.Empty<StructureFinding>(), @"C:\spec.md", json: false)
            .Should().Contain("0");
    }

    [Fact]
    public void Json_emits_structured_findings()
    {
        var json = StructureCheckExporter.Build(Findings, @"C:\path\spec.md", json: true);

        var root = JsonDocument.Parse(json).RootElement;
        root.GetProperty("source").GetString().Should().Be(@"C:\path\spec.md");
        root.GetProperty("findingCount").GetInt32().Should().Be(2);
        root.GetProperty("findings")[0].GetProperty("rule").GetString().Should().Be("skipped-level");
        root.GetProperty("findings")[0].GetProperty("line").GetInt32().Should().Be(3);
    }
}
