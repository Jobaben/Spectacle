using System.Text.Json;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class LinkRefCheckExporterTests
{
    private static readonly UndefinedReference[] Sample =
    {
        new("[the docs][api]", "api", 4),
    };

    [Fact]
    public void Text_states_the_count_and_lists_each_reference()
    {
        var text = LinkRefCheckExporter.Build(Sample, "spec.md", json: false);

        text.Should().Contain("spec.md");
        text.Should().Contain("1 undefined reference");
        text.Should().Contain("line 4");
        text.Should().Contain("[the docs][api]");
        text.Should().Contain("api");
    }

    [Fact]
    public void Text_reports_a_clean_count_when_empty()
    {
        LinkRefCheckExporter.Build(System.Array.Empty<UndefinedReference>(), "spec.md", json: false)
            .Should().Contain("0 undefined reference");
    }

    [Fact]
    public void Json_carries_source_count_and_references()
    {
        var json = LinkRefCheckExporter.Build(Sample, "spec.md", json: true);

        var root = JsonDocument.Parse(json).RootElement;
        root.GetProperty("source").GetString().Should().Be("spec.md");
        root.GetProperty("count").GetInt32().Should().Be(1);
        var reference = root.GetProperty("references")[0];
        reference.GetProperty("label").GetString().Should().Be("api");
        reference.GetProperty("line").GetInt32().Should().Be(4);
    }
}
