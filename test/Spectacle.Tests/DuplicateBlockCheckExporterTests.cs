using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class DuplicateBlockCheckExporterTests
{
    [Fact]
    public void Text_with_no_duplicates_reports_zero()
    {
        var output = DuplicateBlockCheckExporter.Build(new List<DuplicateBlock>(), "spec.md", json: false);

        output.Should().Be("spec.md — duplication: 0 repeated block(s)");
    }

    [Fact]
    public void Text_lists_each_duplicate_with_both_lines()
    {
        var dups = new List<DuplicateBlock>
        {
            new("paragraph", Line: 9, FirstLine: 3, Text: "The requirement is stated."),
        };

        var output = DuplicateBlockCheckExporter.Build(dups, "spec.md", json: false);

        output.Should().Contain("spec.md — duplication: 1 repeated block(s)");
        output.Should().Contain("line 9");
        output.Should().Contain("[paragraph]");
        output.Should().Contain("duplicate of line 3");
        output.Should().Contain("The requirement is stated.");
    }

    [Fact]
    public void Text_collapses_a_multiline_block_to_its_first_line()
    {
        var dups = new List<DuplicateBlock>
        {
            new("code", Line: 12, FirstLine: 4, Text: "var x = 1;\nvar y = 2;"),
        };

        var output = DuplicateBlockCheckExporter.Build(dups, "spec.md", json: false);

        output.Should().Contain("var x = 1; …");
        output.Should().NotContain("var y = 2;");
    }

    [Fact]
    public void Json_carries_camelcase_fields()
    {
        var dups = new List<DuplicateBlock>
        {
            new("paragraph", Line: 9, FirstLine: 3, Text: "Repeated text."),
        };

        var json = DuplicateBlockCheckExporter.Build(dups, "spec.md", json: true);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("source").GetString().Should().Be("spec.md");
        root.GetProperty("duplicateCount").GetInt32().Should().Be(1);
        var first = root.GetProperty("duplicates")[0];
        first.GetProperty("kind").GetString().Should().Be("paragraph");
        first.GetProperty("line").GetInt32().Should().Be(9);
        first.GetProperty("firstLine").GetInt32().Should().Be(3);
        first.GetProperty("text").GetString().Should().Be("Repeated text.");
    }
}
