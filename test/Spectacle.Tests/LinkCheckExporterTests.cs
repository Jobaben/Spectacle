using System;
using System.Text.Json;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class LinkCheckExporterTests
{
    private static readonly BrokenLink[] Broken =
    {
        new(Target: "#missing", Line: 3, Reason: "anchor has no matching heading or id"),
        new(Target: "", Line: 9, Reason: "empty link target"),
    };

    [Fact]
    public void Text_lists_broken_links_with_lines()
    {
        var text = LinkCheckExporter.Build(Broken, @"C:\path\spec.md", json: false);

        text.Should().Contain("spec.md");
        text.Should().Contain("#missing");
        text.Should().Contain("3");
        text.Should().Contain("9");
    }

    [Fact]
    public void Text_reports_clean_when_none()
    {
        LinkCheckExporter.Build(Array.Empty<BrokenLink>(), @"C:\spec.md", json: false)
            .Should().Contain("0");
    }

    [Fact]
    public void Json_emits_structured_broken_links()
    {
        var json = LinkCheckExporter.Build(Broken, @"C:\path\spec.md", json: true);

        var root = JsonDocument.Parse(json).RootElement;
        root.GetProperty("source").GetString().Should().Be(@"C:\path\spec.md");
        root.GetProperty("brokenCount").GetInt32().Should().Be(2);
        var first = root.GetProperty("broken")[0];
        first.GetProperty("target").GetString().Should().Be("#missing");
        first.GetProperty("line").GetInt32().Should().Be(3);
        first.GetProperty("reason").GetString().Should().NotBeNullOrEmpty();
    }
}
