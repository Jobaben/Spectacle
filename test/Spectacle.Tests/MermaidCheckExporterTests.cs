using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Spectacle.Checks;
using Spectacle.Export;
using Xunit;

namespace Spectacle.Tests;

public class MermaidCheckExporterTests
{
    private static readonly List<MermaidIssue> Two = new()
    {
        new(Line: 7, Rule: MermaidChecker.MissingDescriptionRule, Message: "diagram has no accTitle or accDescr"),
        new(Line: 8, Rule: MermaidChecker.UnknownTypeRule, Message: "unknown mermaid diagram type: 'bogus'"),
    };

    [Fact]
    public void Text_with_no_findings_reports_zero()
    {
        // Substring (not exact) match, mirroring the other exporter tests: the text builder
        // ends each line with Environment.NewLine, which is \r\n on Windows.
        MermaidCheckExporter.Build(new List<MermaidIssue>(), "spec.md", json: false)
            .Should().Contain("spec.md — mermaid: 0 issue(s)");
    }

    [Fact]
    public void Text_lists_each_issue_with_line_and_rule()
    {
        var output = MermaidCheckExporter.Build(Two, "spec.md", json: false);

        output.Should().Contain("spec.md — mermaid: 2 issue(s)");
        output.Should().Contain("line 7");
        output.Should().Contain(MermaidChecker.MissingDescriptionRule);
        output.Should().Contain("line 8");
        output.Should().Contain("unknown mermaid diagram type: 'bogus'");
    }

    [Fact]
    public void Text_reports_only_the_file_name()
    {
        MermaidCheckExporter.Build(Two, "/deep/path/to/spec.md", json: false)
            .Should().Contain("spec.md — mermaid").And.NotContain("/deep/path");
    }

    [Fact]
    public void Json_carries_camelcase_fields()
    {
        var json = MermaidCheckExporter.Build(Two, "spec.md", json: true);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("source").GetString().Should().Be("spec.md");
        root.GetProperty("issueCount").GetInt32().Should().Be(2);
        var first = root.GetProperty("issues")[0];
        first.GetProperty("line").GetInt32().Should().Be(7);
        first.GetProperty("rule").GetString().Should().Be(MermaidChecker.MissingDescriptionRule);
        first.GetProperty("message").GetString().Should().Contain("accDescr");
    }
}
