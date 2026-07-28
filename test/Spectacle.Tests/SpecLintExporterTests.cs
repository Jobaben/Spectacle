using System;
using System.Text.Json;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class SpecLintExporterTests
{
    private static readonly SpecLintFinding[] Findings =
    {
        new("placeholder", 5, "placeholder marker 'TODO'"),
        new("empty-section", 9, "heading has no content"),
    };

    [Fact]
    public void Text_lists_each_finding_with_line_and_rule()
    {
        var text = SpecLintExporter.Build(Findings, @"C:\path\spec.md", json: false);

        text.Should().Contain("spec.md");
        text.Should().Contain("5");
        text.Should().Contain("placeholder");
        text.Should().Contain("9");
        text.Should().Contain("empty-section");
    }

    [Fact]
    public void Text_reports_clean_when_no_findings()
    {
        var text = SpecLintExporter.Build(Array.Empty<SpecLintFinding>(), @"C:\spec.md", json: false);

        text.Should().Contain("0");
    }

    [Fact]
    public void Json_emits_structured_findings()
    {
        var json = SpecLintExporter.Build(Findings, @"C:\path\spec.md", json: true);

        var root = JsonDocument.Parse(json).RootElement;
        root.GetProperty("source").GetString().Should().Be(@"C:\path\spec.md");
        root.GetProperty("findingCount").GetInt32().Should().Be(2);
        var first = root.GetProperty("findings")[0];
        first.GetProperty("rule").GetString().Should().Be("placeholder");
        first.GetProperty("line").GetInt32().Should().Be(5);
        first.GetProperty("message").GetString().Should().Contain("TODO");
    }
}
