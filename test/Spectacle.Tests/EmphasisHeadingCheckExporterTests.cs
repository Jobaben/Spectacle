using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class EmphasisHeadingCheckExporterTests
{
    [Fact]
    public void Text_output_lists_each_finding_with_its_line()
    {
        var findings = new List<EmphasisHeading> { new("Overview", 3), new("Goals", 7) };

        var text = EmphasisHeadingCheckExporter.Build(findings, "spec.md", json: false);

        text.Should().Contain("spec.md — emphasis headings: 2");
        text.Should().Contain("line 3");
        text.Should().Contain("Overview");
        text.Should().Contain("line 7");
        text.Should().Contain("Goals");
    }

    [Fact]
    public void Text_output_reports_a_clean_spec()
    {
        var text = EmphasisHeadingCheckExporter.Build(new List<EmphasisHeading>(), "spec.md", json: false);

        text.Should().Contain("emphasis headings: 0");
    }

    [Fact]
    public void Json_output_carries_count_and_findings()
    {
        var findings = new List<EmphasisHeading> { new("Overview", 3) };

        var json = EmphasisHeadingCheckExporter.Build(findings, "spec.md", json: true);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("source").GetString().Should().Be("spec.md");
        doc.RootElement.GetProperty("count").GetInt32().Should().Be(1);
        var first = doc.RootElement.GetProperty("headings")[0];
        first.GetProperty("text").GetString().Should().Be("Overview");
        first.GetProperty("line").GetInt32().Should().Be(3);
    }
}
