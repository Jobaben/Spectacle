using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class ProseCheckExporterTests
{
    [Fact]
    public void Text_with_no_findings_reports_zero_and_marks_advisory()
    {
        var output = ProseCheckExporter.Build(new List<ProseFinding>(), "spec.md", json: false);

        // Substring (not exact) match: the text builder ends each line with
        // Environment.NewLine, which is \r\n on Windows.
        output.Should().Contain("spec.md — prose: 0 vague/hedging phrase(s) [advisory]");
    }

    [Fact]
    public void Text_lists_each_finding_with_line_rule_and_phrase()
    {
        var findings = new List<ProseFinding>
        {
            new(ProseChecker.HedgeRule, Line: 7, Phrase: "should probably", Message: "hedging language 'should probably'"),
        };

        var output = ProseCheckExporter.Build(findings, "spec.md", json: false);

        output.Should().Contain("spec.md — prose: 1 vague/hedging phrase(s) [advisory]");
        output.Should().Contain("line 7");
        output.Should().Contain("[hedge]");
        output.Should().Contain("should probably");
    }

    [Fact]
    public void Json_carries_camelcase_fields()
    {
        var findings = new List<ProseFinding>
        {
            new(ProseChecker.WeaselRule, Line: 3, Phrase: "etc.", Message: "vague wording 'etc.'"),
        };

        var json = ProseCheckExporter.Build(findings, "spec.md", json: true);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("source").GetString().Should().Be("spec.md");
        root.GetProperty("count").GetInt32().Should().Be(1);
        var first = root.GetProperty("findings")[0];
        first.GetProperty("rule").GetString().Should().Be("weasel");
        first.GetProperty("line").GetInt32().Should().Be(3);
        first.GetProperty("phrase").GetString().Should().Be("etc.");
    }
}
