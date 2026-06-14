using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class LinkPathCheckExporterTests
{
    private static readonly IReadOnlyList<BrokenPath> Broken = new[]
    {
        new BrokenPath("./missing.md", 4, "relative link target not found on disk"),
        new BrokenPath("img/gone.png", 7, "relative image target not found on disk"),
    };

    [Fact]
    public void Text_shows_filename_count_and_each_target()
    {
        var text = LinkPathCheckExporter.Build(Broken, @"C:\path\spec.md", json: false);

        text.Should().Contain("spec.md");
        text.Should().Contain("2 broken");
        text.Should().Contain("line 4");
        text.Should().Contain("./missing.md");
        text.Should().Contain("line 7");
        text.Should().Contain("img/gone.png");
    }

    [Fact]
    public void Json_emits_broken_count_and_array()
    {
        var json = LinkPathCheckExporter.Build(Broken, @"C:\path\spec.md", json: true);

        var root = JsonDocument.Parse(json).RootElement;
        root.GetProperty("source").GetString().Should().Be(@"C:\path\spec.md");
        root.GetProperty("brokenCount").GetInt32().Should().Be(2);
        root.GetProperty("broken").GetArrayLength().Should().Be(2);
        root.GetProperty("broken")[0].GetProperty("target").GetString().Should().Be("./missing.md");
        root.GetProperty("broken")[0].GetProperty("line").GetInt32().Should().Be(4);
    }

    [Fact]
    public void Text_reports_zero_broken_cleanly()
    {
        LinkPathCheckExporter.Build(System.Array.Empty<BrokenPath>(), "spec.md", json: false)
            .Should().Contain("0 broken");
    }
}
