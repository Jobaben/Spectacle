using System;
using System.Text.Json;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class TocCheckExporterTests
{
    private static readonly TocIssue[] Issues =
    {
        new(Rule: TocChecker.StaleEntryRule, Line: 6, Message: "TOC entry 'Gone' points to '#gone', which matches no heading", Anchor: "#gone"),
        new(Rule: TocChecker.MissingEntryRule, Line: 14, Message: "heading 'Details' has no entry in the table of contents", Anchor: "#details"),
    };

    [Fact]
    public void Text_lists_issues_with_lines_and_rules()
    {
        var text = TocCheckExporter.Build(Issues, @"C:\path\spec.md", json: false);

        text.Should().Contain("spec.md");
        text.Should().Contain(TocChecker.StaleEntryRule);
        text.Should().Contain(TocChecker.MissingEntryRule);
        text.Should().Contain("6");
        text.Should().Contain("14");
    }

    [Fact]
    public void Text_reports_clean_when_none()
    {
        TocCheckExporter.Build(Array.Empty<TocIssue>(), @"C:\spec.md", json: false)
            .Should().Contain("0");
    }

    [Fact]
    public void Json_emits_structured_issues()
    {
        var json = TocCheckExporter.Build(Issues, @"C:\path\spec.md", json: true);

        var root = JsonDocument.Parse(json).RootElement;
        root.GetProperty("source").GetString().Should().Be(@"C:\path\spec.md");
        root.GetProperty("issueCount").GetInt32().Should().Be(2);
        var first = root.GetProperty("issues")[0];
        first.GetProperty("rule").GetString().Should().Be(TocChecker.StaleEntryRule);
        first.GetProperty("line").GetInt32().Should().Be(6);
        first.GetProperty("anchor").GetString().Should().Be("#gone");
        first.GetProperty("message").GetString().Should().NotBeNullOrEmpty();
    }
}
