using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Spectacle.Checks;
using Spectacle.Cli;
using Spectacle.Export;
using Spectacle.Gate;
using Xunit;

namespace Spectacle.Tests;

/// <summary>
/// End-to-end coverage of the workflow gate on real files: a project declares its contract in
/// <c>.spectacle.json</c>, a generator writes a document, the gate grades it, the brief describes
/// the revision, the revision is applied, and the gate passes.
///
/// This exercises the wiring the unit tests cannot: config discovery from a document's own
/// location, the front-matter template reaching the checker, grades reaching the verdict, and the
/// same verdict driving the reader's overlay.
/// </summary>
public class GateWorkflowE2ETests : IDisposable
{
    private readonly string _tmp =
        Path.Combine(Path.GetTempPath(), "spectacle-gate-e2e-" + Guid.NewGuid().ToString("N"));

    public GateWorkflowE2ETests() => Directory.CreateDirectory(_tmp);

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_tmp, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private void WriteConfig(string json) => Write(ConfigLocator.FileName, json);

    // The same resolution Program applies: config from the document's own location, front-matter
    // and section templates from that config, grades from its severity map.
    private GateVerdict Gate(string path)
    {
        var config = ConfigLocator.Resolve(path, null);
        var content = File.ReadAllText(path);
        var report = ReviewReport.Compute(
            content,
            relative => File.Exists(Path.Combine(Path.GetDirectoryName(path)!, relative)),
            config.RequiredSections,
            ReviewChecks.Resolve(Array.Empty<string>(), Array.Empty<string>(), config.DisabledChecks),
            config.RequiredFrontMatter);

        return GateVerdict.Compute(
            Path.GetFileName(path), report,
            GatePolicy.Create(config.Severity, config.FailOn),
            FrontMatter.Parse(content));
    }

    [Fact]
    public void A_project_metadata_contract_is_enforced_on_a_generated_document()
    {
        WriteConfig("""
            { "requiredFrontMatter": ["workflow", "stage", "run.model"] }
            """);
        var path = Write("draft.md", "---\nworkflow: spec-writer\nstage:\n---\n\n# Auth\n\nThe token is signed.\n");

        var verdict = Gate(path);

        verdict.Passed.Should().BeFalse();
        verdict.Findings.Select(f => f.RuleId).Should().Contain(new[]
        {
            "front-matter/empty-value",   // stage is present but blank
            "front-matter/missing-key",   // run.model was never declared
        });
    }

    [Fact]
    public void A_document_that_honours_the_contract_passes()
    {
        WriteConfig("""
            { "requiredFrontMatter": ["workflow", "stage", "run.model"] }
            """);
        var path = Write("draft.md",
            "---\nworkflow: spec-writer\nstage: draft\nrun:\n  model: opus\n---\n\n# Auth\n\nThe token is signed.\n");

        var verdict = Gate(path);

        verdict.Passed.Should().BeTrue();
        verdict.Metadata.Select(m => m.Key).Should().Contain("run.model");
    }

    [Fact]
    public void A_project_without_a_config_is_unaffected_by_the_metadata_contract()
    {
        var path = Write("plain.md", "# Auth\n\nThe token is signed.\n");

        Gate(path).Passed.Should().BeTrue();
    }

    [Fact]
    public void A_graded_project_reports_a_downgraded_rule_without_blocking_on_it()
    {
        WriteConfig("""
            { "severity": { "bare-urls": "warning" }, "failOn": "error" }
            """);
        var path = Write("draft.md", "# Auth\n\nSee https://example.com for the shape.\n");

        var verdict = Gate(path);

        verdict.Passed.Should().BeTrue();
        verdict.WarningCount.Should().BeGreaterThan(0);
        verdict.AppliedGrades.Should().Contain("bare-urls=warning");
    }

    [Fact]
    public void A_project_that_blocks_on_warnings_fails_the_same_document()
    {
        WriteConfig("""
            { "severity": { "bare-urls": "warning" }, "failOn": "warning" }
            """);
        var path = Write("draft.md", "# Auth\n\nSee https://example.com for the shape.\n");

        Gate(path).Passed.Should().BeFalse();
    }

    [Fact]
    public void Generation_residue_fails_the_gate_and_the_brief_says_how_to_fix_it()
    {
        var path = Write("draft.md",
            "# Auth\n\nCertainly! Here is the updated design.\n\nThe scope is {{scope}}.\n");

        var verdict = Gate(path);
        verdict.Passed.Should().BeFalse();
        verdict.Findings.Select(f => f.RuleId).Should().Contain(new[]
        {
            "ai-artifacts/assistant-voice",
            "ai-artifacts/unfilled-template",
        });

        var brief = FixBriefExporter.Build(verdict, json: false);
        brief.Should().Contain("## Required fixes (2)");
        brief.Should().Contain(RuleCatalog.RemedyOf("ai-artifacts/assistant-voice"));
    }

    [Fact]
    public void The_write_gate_revise_gate_loop_closes()
    {
        WriteConfig("""
            { "requiredFrontMatter": ["stage"] }
            """);

        // 1. The generator writes a document carrying its own residue and an incomplete header.
        var path = Write("draft.md",
            "---\nstage:\n---\n\n# Auth\n\nCertainly! The token is signed and validated.\n");

        // 2. The gate refuses it and hands out a revision brief.
        var first = Gate(path);
        first.Passed.Should().BeFalse();
        var brief = FixBriefExporter.Build(first, json: true);
        var instructions = JsonDocument.Parse(brief).RootElement.GetProperty("instructions");
        instructions.GetArrayLength().Should().Be(2);

        // 3. A revision applies exactly what the brief asked for.
        File.WriteAllText(path, "---\nstage: draft\n---\n\n# Auth\n\nThe token is signed and validated.\n");

        // 4. The gate now passes, and the brief says there is nothing left to do.
        var second = Gate(path);
        second.Passed.Should().BeTrue();
        FixBriefExporter.Build(second, json: false)
            .Should().Contain("No findings. Leave the document unchanged.");
    }

    [Fact]
    public void A_folder_of_generated_documents_gates_as_one_set()
    {
        Write("specs/a.md", "# A\n\nThe token is signed.\n");
        Write("specs/b.md", "# B\n\nTODO: decide the lifetime.\n");

        var specs = BatchReview.EnumerateSpecs(Path.Combine(_tmp, "specs"));
        var batch = new GateBatch(specs.Select(Gate).ToList());

        batch.Verdicts.Should().HaveCount(2);
        batch.Passed.Should().BeFalse();
        batch.Failed.Should().HaveCount(1);
        batch.Failed[0].SourcePath.Should().Be("b.md");
    }

    [Fact]
    public void The_readers_verdict_matches_the_commands_verdict()
    {
        // If these could disagree, the badge would be a second opinion nobody asked for.
        WriteConfig("""
            { "requiredFrontMatter": ["stage"], "severity": { "bare-urls": "warning" } }
            """);
        var path = Write("draft.md", "---\nstage:\n---\n\n# Auth\n\nSee https://example.com for the shape.\n");

        var fromCommand = Gate(path);
        var fromReader = LiveGate.Evaluate(File.ReadAllText(path), _tmp, "draft.md");

        fromReader.Passed.Should().Be(fromCommand.Passed);
        fromReader.BlockingCount.Should().Be(fromCommand.BlockingCount);
        fromReader.Findings.Select(f => (f.RuleId, f.Line, f.Severity))
            .Should().Equal(fromCommand.Findings.Select(f => (f.RuleId, f.Line, f.Severity)));
    }

    [Fact]
    public void The_readers_verdict_survives_a_broken_config()
    {
        WriteConfig("{ not json at all");
        var path = Write("draft.md", "# Auth\n\nThe token is signed.\n");

        // A broken config must degrade the badge, never take down the reader.
        var verdict = LiveGate.Evaluate(File.ReadAllText(path), _tmp, "draft.md");

        verdict.Passed.Should().BeTrue();
    }

    [Fact]
    public void A_disabled_check_is_reported_as_off_rather_than_silently_passing()
    {
        WriteConfig("""
            { "disabledChecks": ["lint"] }
            """);
        var path = Write("draft.md", "# Auth\n\nTODO: decide the lifetime.\n");

        var verdict = Gate(path);

        verdict.Passed.Should().BeTrue();
        verdict.CoverageReduced.Should().BeTrue();
        verdict.SkippedChecks.Should().Contain("lint");

        // Every output has to carry the caveat, or a green result reads as full coverage.
        GateExporter.Build(new GateBatch(new[] { verdict }), "1.0.0", json: false)
            .Should().Contain("checks off:").And.Contain("lint");
    }

    [Fact]
    public void Sarif_and_the_gate_agree_on_severity_after_grading()
    {
        WriteConfig("""
            { "severity": { "lint": "warning" } }
            """);
        var path = Write("draft.md", "# Auth\n\nTODO: decide the lifetime.\n");

        var config = ConfigLocator.Resolve(path, null);
        var policy = GatePolicy.Create(config.Severity, config.FailOn);
        var report = ReviewReport.Compute(File.ReadAllText(path));

        var sarif = SarifExporter.Build(new[] { new BatchReviewEntry("draft.md", report) }, "1.0.0", policy);
        var level = JsonDocument.Parse(sarif).RootElement
            .GetProperty("runs")[0].GetProperty("results").EnumerateArray()
            .First(r => r.GetProperty("ruleId").GetString() == "lint/placeholder")
            .GetProperty("level").GetString();

        level.Should().Be("warning");
        Gate(path).Passed.Should().BeTrue();
    }
}
