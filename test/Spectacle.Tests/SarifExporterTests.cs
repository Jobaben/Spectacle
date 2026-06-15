using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class SarifExporterTests
{
    private static ReviewReport SampleReport() => new(
        Lint: new[] { new SpecLintFinding("placeholder", 4, "placeholder marker 'TODO'") },
        Structure: new[] { new StructureFinding("multiple-h1", 9, "more than one top-level (h1) heading") },
        Links: new[] { new BrokenLink("#missing", 12, "no matching heading or id") },
        Tables: new[] { new TableIssue(20, "row has 2 cells, header has 3") },
        Fences: new[] { new FenceIssue(30, "unclosed-fence", "code fence opened here is never closed") },
        Paths: new[] { new BrokenPath("img/logo.png", 40, "target does not exist") },
        Duplication: System.Array.Empty<DuplicateBlock>(),
        AltText: System.Array.Empty<ImageWithoutAlt>(),
        EmphasisHeadings: System.Array.Empty<EmphasisHeading>(),
        Sections: System.Array.Empty<MissingSection>(),
        ChecklistTotal: 3,
        ChecklistDone: 1);

    private static JsonElement Run(string sarif) =>
        JsonDocument.Parse(sarif).RootElement.GetProperty("runs")[0];

    [Fact]
    public void Emits_valid_sarif_envelope()
    {
        var sarif = SarifExporter.Build(new[] { new BatchReviewEntry("spec.md", SampleReport()) }, "1.2.3");

        var root = JsonDocument.Parse(sarif).RootElement;
        root.GetProperty("$schema").GetString().Should().Contain("sarif-2.1.0");
        root.GetProperty("version").GetString().Should().Be("2.1.0");
        root.GetProperty("runs").GetArrayLength().Should().Be(1);

        var driver = Run(sarif).GetProperty("tool").GetProperty("driver");
        driver.GetProperty("name").GetString().Should().Be("Spectacle");
        driver.GetProperty("version").GetString().Should().Be("1.2.3");
        driver.GetProperty("rules").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public void Maps_every_finding_to_a_result_with_ruleId_level_and_line()
    {
        var sarif = SarifExporter.Build(new[] { new BatchReviewEntry("spec.md", SampleReport()) }, "1.0.0");
        var results = Run(sarif).GetProperty("results");

        // One result per finding across all six checks (the checklist is informational).
        results.GetArrayLength().Should().Be(6);

        var ruleIds = results.EnumerateArray().Select(r => r.GetProperty("ruleId").GetString()).ToList();
        ruleIds.Should().Contain(new[]
        {
            "lint/placeholder", "structure/multiple-h1", "links",
            "tables", "fences/unclosed-fence", "paths",
        });

        var first = results[0];
        first.GetProperty("level").GetString().Should().Be("error");
        first.GetProperty("message").GetProperty("text").GetString().Should().NotBeNullOrEmpty();
        first.GetProperty("locations")[0].GetProperty("physicalLocation")
            .GetProperty("region").GetProperty("startLine").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public void Emits_results_for_the_new_check_categories()
    {
        // duplication + alt-text + emphasis-heading from content, plus a missing section.
        const string content =
            "# Title\n\n**Overview**\n\nThe quick brown fox jumps over the lazy dog here.\n\n" +
            "![](d.png)\n\nThe quick brown fox jumps over the lazy dog here.\n";
        var report = ReviewReport.Compute(content, _ => true, new[] { "Acceptance Criteria" });

        var sarif = SarifExporter.Build(new[] { new BatchReviewEntry("spec.md", report) }, "1.0.0");
        var ruleIds = Run(sarif).GetProperty("results").EnumerateArray()
            .Select(r => r.GetProperty("ruleId").GetString()).ToList();

        ruleIds.Should().Contain(new[] { "duplication", "alt-text", "emphasis-heading", "sections" });

        // The catalogue advertises the new rules even when they did not fire.
        var catalogIds = Run(sarif).GetProperty("tool").GetProperty("driver").GetProperty("rules")
            .EnumerateArray().Select(r => r.GetProperty("id").GetString()).ToList();
        catalogIds.Should().Contain(new[] { "duplication", "alt-text", "emphasis-heading", "sections" });
    }

    [Fact]
    public void Missing_section_result_carries_a_valid_line_one_region()
    {
        var report = ReviewReport.Compute("# Title\n\nText.\n", _ => true, new[] { "Acceptance Criteria" });

        var sarif = SarifExporter.Build(new[] { new BatchReviewEntry("spec.md", report) }, "1.0.0");
        var section = Run(sarif).GetProperty("results").EnumerateArray()
            .Single(r => r.GetProperty("ruleId").GetString() == "sections");

        section.GetProperty("locations")[0].GetProperty("physicalLocation")
            .GetProperty("region").GetProperty("startLine").GetInt32().Should().Be(1);
    }

    [Fact]
    public void Normalizes_backslash_paths_to_forward_slash_uri()
    {
        var sarif = SarifExporter.Build(
            new[] { new BatchReviewEntry(@"specs\sub\spec.md", SampleReport()) }, "1.0.0");

        var uri = Run(sarif).GetProperty("results")[0].GetProperty("locations")[0]
            .GetProperty("physicalLocation").GetProperty("artifactLocation").GetProperty("uri").GetString();
        uri.Should().Be("specs/sub/spec.md");
    }

    [Fact]
    public void Clean_report_emits_zero_results_but_valid_envelope()
    {
        var clean = ReviewReport.Compute("# Title\n\nAll good.\n");
        var sarif = SarifExporter.Build(new[] { new BatchReviewEntry("spec.md", clean) }, "1.0.0");

        Run(sarif).GetProperty("results").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void Batch_carries_results_from_every_file()
    {
        var entries = new[]
        {
            new BatchReviewEntry("a.md", SampleReport()),
            new BatchReviewEntry("b.md", SampleReport()),
        };

        var sarif = SarifExporter.Build(entries, "1.0.0");
        var results = Run(sarif).GetProperty("results");

        results.GetArrayLength().Should().Be(12);
        var uris = results.EnumerateArray()
            .Select(r => r.GetProperty("locations")[0].GetProperty("physicalLocation")
                .GetProperty("artifactLocation").GetProperty("uri").GetString())
            .Distinct();
        uris.Should().BeEquivalentTo(new[] { "a.md", "b.md" });
    }

    [Fact]
    public void Includes_toc_findings_as_results_and_catalogue_rules()
    {
        const string content = "# Spec\n\n## Contents\n\n- [Gone](#gone)\n\n## Overview\n\ntext\n";
        var sarif = SarifExporter.Build(
            new[] { new BatchReviewEntry("spec.md", ReviewReport.Compute(content)) }, "1.0.0");

        var run = Run(sarif);
        var ruleIds = run.GetProperty("results").EnumerateArray()
            .Select(r => r.GetProperty("ruleId").GetString()).ToList();
        ruleIds.Should().Contain("toc/stale-toc-entry");

        var catalogue = run.GetProperty("tool").GetProperty("driver").GetProperty("rules")
            .EnumerateArray().Select(r => r.GetProperty("id").GetString()).ToList();
        catalogue.Should().Contain("toc/stale-toc-entry").And.Contain("toc/missing-from-toc");
    }
}
