using System.Text.Json;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class BareUrlCheckExporterTests
{
    private static readonly BareUrl[] Sample =
    {
        new("https://example.com", 4),
    };

    [Fact]
    public void Text_states_the_count_and_lists_each_url()
    {
        var text = BareUrlCheckExporter.Build(Sample, "spec.md", json: false);

        text.Should().Contain("spec.md");
        text.Should().Contain("1 bare URL");
        text.Should().Contain("line 4");
        text.Should().Contain("https://example.com");
    }

    [Fact]
    public void Text_reports_a_clean_count_when_empty()
    {
        BareUrlCheckExporter.Build(System.Array.Empty<BareUrl>(), "spec.md", json: false)
            .Should().Contain("0 bare URL");
    }

    [Fact]
    public void Json_carries_source_count_and_urls()
    {
        var json = BareUrlCheckExporter.Build(Sample, "spec.md", json: true);

        var root = JsonDocument.Parse(json).RootElement;
        root.GetProperty("source").GetString().Should().Be("spec.md");
        root.GetProperty("count").GetInt32().Should().Be(1);
        var url = root.GetProperty("urls")[0];
        url.GetProperty("url").GetString().Should().Be("https://example.com");
        url.GetProperty("line").GetInt32().Should().Be(4);
    }
}
