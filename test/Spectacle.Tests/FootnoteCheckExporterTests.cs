using System.Text.Json;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class FootnoteCheckExporterTests
{
    private static readonly UndefinedFootnote[] Sample =
    {
        new("src", 4),
    };

    [Fact]
    public void Text_states_the_count_and_lists_each_footnote()
    {
        var text = FootnoteCheckExporter.Build(Sample, "spec.md", json: false);

        text.Should().Contain("spec.md");
        text.Should().Contain("1 undefined footnote");
        text.Should().Contain("line 4");
        text.Should().Contain("[^src]");
    }

    [Fact]
    public void Text_reports_a_clean_count_when_empty()
    {
        FootnoteCheckExporter.Build(System.Array.Empty<UndefinedFootnote>(), "spec.md", json: false)
            .Should().Contain("0 undefined footnote");
    }

    [Fact]
    public void Json_carries_source_count_and_footnotes()
    {
        var json = FootnoteCheckExporter.Build(Sample, "spec.md", json: true);

        var root = JsonDocument.Parse(json).RootElement;
        root.GetProperty("source").GetString().Should().Be("spec.md");
        root.GetProperty("count").GetInt32().Should().Be(1);
        var footnote = root.GetProperty("footnotes")[0];
        footnote.GetProperty("label").GetString().Should().Be("src");
        footnote.GetProperty("line").GetInt32().Should().Be(4);
    }
}
