using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class AltTextCheckExporterTests
{
    [Fact]
    public void Text_with_no_findings_reports_zero()
    {
        var output = AltTextCheckExporter.Build(new List<ImageWithoutAlt>(), "spec.md", json: false);

        // Substring (not exact) match, mirroring the other exporter tests: the text builder
        // ends each line with Environment.NewLine, which is \r\n on Windows.
        output.Should().Contain("spec.md — alt text: 0 image(s) missing alt text");
    }

    [Fact]
    public void Text_lists_each_image_with_line_and_target()
    {
        var images = new List<ImageWithoutAlt>
        {
            new("screenshot.png", Line: 12),
        };

        var output = AltTextCheckExporter.Build(images, "spec.md", json: false);

        output.Should().Contain("spec.md — alt text: 1 image(s) missing alt text");
        output.Should().Contain("line 12");
        output.Should().Contain("screenshot.png");
    }

    [Fact]
    public void Text_shows_placeholder_for_an_empty_target()
    {
        var images = new List<ImageWithoutAlt> { new("", Line: 4) };

        var output = AltTextCheckExporter.Build(images, "spec.md", json: false);

        output.Should().Contain("(no target)");
    }

    [Fact]
    public void Json_carries_camelcase_fields()
    {
        var images = new List<ImageWithoutAlt> { new("screenshot.png", Line: 12) };

        var json = AltTextCheckExporter.Build(images, "spec.md", json: true);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("source").GetString().Should().Be("spec.md");
        root.GetProperty("missingCount").GetInt32().Should().Be(1);
        var first = root.GetProperty("images")[0];
        first.GetProperty("target").GetString().Should().Be("screenshot.png");
        first.GetProperty("line").GetInt32().Should().Be(12);
    }
}
