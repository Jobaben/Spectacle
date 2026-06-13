using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class OutlineExporterTests
{
    private const string Doc = "# Title\n\nIntro.\n\n## Section A\n\nText.\n\n### Detail\n\nMore.\n\n## Section B\n";

    private static System.Collections.Generic.IReadOnlyList<OutlineEntry> Outline(string md) =>
        new MdRenderer().Render(md).Outline;

    [Fact]
    public void Text_indents_by_heading_level_and_lists_all_headings()
    {
        var text = OutlineExporter.Build(Outline(Doc), @"C:\path\spec.md", json: false);

        text.Should().Contain("spec.md");
        text.Should().Contain("Title");
        text.Should().Contain("Section A");
        text.Should().Contain("Detail");
        text.Should().Contain("Section B");
        // A level-3 heading is indented more than a level-2 heading.
        var lines = text.Split('\n');
        var detail = lines.First(l => l.Contains("Detail"));
        var sectionA = lines.First(l => l.Contains("Section A"));
        detail.IndexOf("Detail").Should().BeGreaterThan(sectionA.IndexOf("Section A"));
    }

    [Fact]
    public void Json_emits_level_text_and_line_in_document_order()
    {
        var json = OutlineExporter.Build(Outline(Doc), @"C:\path\spec.md", json: true);

        var root = JsonDocument.Parse(json).RootElement;
        root.GetProperty("source").GetString().Should().Be(@"C:\path\spec.md");
        var headings = root.GetProperty("headings");
        headings.GetArrayLength().Should().Be(4);
        headings[0].GetProperty("level").GetInt32().Should().Be(1);
        headings[0].GetProperty("text").GetString().Should().Be("Title");
        headings[2].GetProperty("level").GetInt32().Should().Be(3);
        headings[2].GetProperty("text").GetString().Should().Be("Detail");
    }

    [Fact]
    public void Empty_document_reports_no_headings()
    {
        var text = OutlineExporter.Build(Outline("Just prose, no headings.\n"), @"C:\spec.md", json: false);
        text.Should().Contain("0");

        var json = OutlineExporter.Build(Outline("Just prose.\n"), @"C:\spec.md", json: true);
        JsonDocument.Parse(json).RootElement.GetProperty("headings").GetArrayLength().Should().Be(0);
    }
}
