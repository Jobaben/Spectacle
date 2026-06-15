using System.Text.Json;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class LinkTextCheckExporterTests
{
    [Fact]
    public void Text_output_names_the_file_and_count()
    {
        const string content = "# Doc\n\n[here](a.md) and [more](b.md)\n";
        var links = LinkTextChecker.Check(content);

        var output = LinkTextCheckExporter.Build(links, "spec.md", json: false);

        output.Should().Contain("spec.md — link text: 2 uninformative link(s)");
        output.Should().Contain("line 3");
    }

    [Fact]
    public void Text_output_for_a_clean_doc_reports_zero()
    {
        var links = LinkTextChecker.Check("# Doc\n\n[the runbook](r.md)\n");

        LinkTextCheckExporter.Build(links, "spec.md", json: false)
            .Should().Contain("0 uninformative link(s)");
    }

    [Fact]
    public void Json_output_is_structured()
    {
        const string content = "# Doc\n\n[click here](a.md)\n";
        var links = LinkTextChecker.Check(content);

        var json = LinkTextCheckExporter.Build(links, "spec.md", json: true);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("source").GetString().Should().Be("spec.md");
        root.GetProperty("uninformativeCount").GetInt32().Should().Be(1);
        root.GetProperty("links").GetArrayLength().Should().Be(1);
        root.GetProperty("links")[0].GetProperty("line").GetInt32().Should().Be(3);
    }
}
