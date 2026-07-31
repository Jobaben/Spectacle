using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Spectacle.Checks;
using Spectacle.Export;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class GateExporterTests
{
    private const string Dirty =
        "# Title\n\nTODO: decide the token lifetime.\n\nSee https://example.com for details.\n\n" +
        "We should probably cache it.\n";

    private static GateVerdict Verdict(string content = Dirty, string path = "spec.md", GatePolicy? policy = null) =>
        GateVerdict.Compute(
            path, ReviewReport.Compute(content), policy ?? GatePolicy.Default, FrontMatter.Parse(content));

    private static GateBatch Batch(params GateVerdict[] verdicts) => new(verdicts);

    // ---------- text ----------

    [Fact]
    public void Text_leads_with_the_verdict_and_the_threshold()
    {
        var text = GateExporter.Build(Batch(Verdict()), "1.0.0", json: false);

        text.Should().StartWith("spec.md — GATE FAIL");
        text.Should().Contain("threshold: error");
        text.Should().Contain("blocking");
    }

    [Fact]
    public void Text_says_pass_for_a_clean_document()
    {
        var text = GateExporter.Build(Batch(Verdict("# Title\n\nA signed token is issued.\n")), "1.0.0", false);

        text.Should().Contain("GATE PASS");
    }

    [Fact]
    public void Text_lists_each_finding_with_severity_line_and_rule()
    {
        var text = GateExporter.Build(Batch(Verdict()), "1.0.0", false);

        text.Should().Contain("error").And.Contain("lint/placeholder").And.Contain("line");
        text.Should().Contain("info").And.Contain("prose/hedge");
    }

    [Fact]
    public void Text_names_the_documents_metadata()
    {
        const string content = "---\nworkflow: spec-writer\n---\n\n# T\n\nTODO: decide.\n";
        var text = GateExporter.Build(Batch(Verdict(content)), "1.0.0", false);

        text.Should().Contain("metadata:").And.Contain("workflow=spec-writer");
    }

    [Fact]
    public void Text_declares_reduced_coverage()
    {
        var checks = ReviewChecks.Resolve(new[] { "lint" }, System.Array.Empty<string>(), System.Array.Empty<string>());
        var report = ReviewReport.Compute("# T\n\nText.\n", _ => true, System.Array.Empty<string>(), checks);
        var text = GateExporter.Build(
            Batch(GateVerdict.Compute("spec.md", report, GatePolicy.Default)), "1.0.0", false);

        text.Should().Contain("coverage:").And.Contain("checks off:");
    }

    [Fact]
    public void Text_points_a_failing_document_at_the_fix_brief()
    {
        GateExporter.Build(Batch(Verdict()), "1.0.0", false).Should().Contain("--fix-brief");
    }

    [Fact]
    public void Text_for_a_set_summarizes_then_details_only_the_failures()
    {
        var clean = Verdict("# Title\n\nA signed token is issued.\n", "clean.md");
        var text = GateExporter.Build(Batch(clean, Verdict()), "1.0.0", false);

        text.Should().StartWith("gate FAIL — 2 document(s), 1 failing");
        text.Should().Contain("pass  clean.md");
        text.Should().Contain("FAIL  spec.md");
        text.Should().Contain("spec.md — GATE FAIL");
    }

    // ---------- json ----------

    private static JsonElement Json(GateBatch batch) =>
        JsonDocument.Parse(GateExporter.Build(batch, "1.2.3", json: true)).RootElement;

    [Fact]
    public void Json_carries_the_verdict_the_tool_version_and_the_counts()
    {
        var root = Json(Batch(Verdict()));

        root.GetProperty("tool").GetString().Should().Be("spectacle");
        root.GetProperty("version").GetString().Should().Be("1.2.3");
        root.GetProperty("gate").GetString().Should().Be("fail");
        root.GetProperty("passed").GetBoolean().Should().BeFalse();
        root.GetProperty("counts").GetProperty("documents").GetInt32().Should().Be(1);
        root.GetProperty("counts").GetProperty("blocking").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public void Json_always_reports_a_set_even_for_one_document()
    {
        // A workflow parses one shape whether it gated a file or a folder.
        var documents = Json(Batch(Verdict())).GetProperty("documents");

        documents.GetArrayLength().Should().Be(1);
        documents[0].GetProperty("source").GetString().Should().Be("spec.md");
        documents[0].GetProperty("failOn").GetString().Should().Be("error");
    }

    [Fact]
    public void Json_findings_carry_the_rule_the_description_the_remedy_and_whether_they_block()
    {
        var findings = Json(Batch(Verdict())).GetProperty("documents")[0].GetProperty("findings");
        var lint = findings.EnumerateArray().First(f => f.GetProperty("rule").GetString() == "lint/placeholder");

        lint.GetProperty("severity").GetString().Should().Be("error");
        lint.GetProperty("blocking").GetBoolean().Should().BeTrue();
        lint.GetProperty("check").GetString().Should().Be("lint");
        lint.GetProperty("line").GetInt32().Should().Be(3);
        lint.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();
        lint.GetProperty("remedy").GetString().Should().NotBeNullOrWhiteSpace();

        var hedge = findings.EnumerateArray().First(f => f.GetProperty("rule").GetString() == "prose/hedge");
        hedge.GetProperty("blocking").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void Json_echoes_the_front_matter_as_an_object()
    {
        const string content = "---\nworkflow: spec-writer\nstage: draft\n---\n\n# T\n\nText.\n";
        var metadata = Json(Batch(Verdict(content))).GetProperty("documents")[0].GetProperty("metadata");

        metadata.GetProperty("workflow").GetString().Should().Be("spec-writer");
        metadata.GetProperty("stage").GetString().Should().Be("draft");
    }

    [Fact]
    public void Json_survives_a_duplicated_front_matter_key()
    {
        // A duplicate key is a finding, not a reason to crash: last value wins, matching what a
        // YAML parser hands the workflow.
        const string content = "---\nstage: draft\nstage: final\n---\n\n# T\n\nText.\n";

        var metadata = Json(Batch(Verdict(content))).GetProperty("documents")[0].GetProperty("metadata");
        metadata.GetProperty("stage").GetString().Should().Be("final");
    }

    [Fact]
    public void Json_reports_the_coverage_that_produced_the_verdict()
    {
        var coverage = Json(Batch(Verdict())).GetProperty("documents")[0].GetProperty("coverage");

        coverage.GetProperty("checksDisabled").GetArrayLength().Should().Be(0);
        coverage.GetProperty("suppressed").GetInt32().Should().Be(0);
    }

    [Fact]
    public void Json_for_a_clean_document_passes()
    {
        var root = Json(Batch(Verdict("# Title\n\nA signed token is issued.\n")));

        root.GetProperty("gate").GetString().Should().Be("pass");
        root.GetProperty("passed").GetBoolean().Should().BeTrue();
    }

    // ---------- markdown ----------

    [Fact]
    public void Markdown_headlines_the_verdict_and_tabulates_the_findings()
    {
        var md = GateExporter.Build(Batch(Verdict()), "1.0.0", json: false, markdown: true);

        md.Should().StartWith("# Gate failed — `spec.md`");
        md.Should().Contain("| Severity | Line | Rule | Finding |");
        md.Should().Contain("`lint/placeholder`");
        md.Should().Contain("threshold `error`");
    }

    [Fact]
    public void Markdown_for_a_set_tabulates_every_document()
    {
        var clean = Verdict("# Title\n\nA signed token is issued.\n", "clean.md");
        var md = GateExporter.Build(Batch(clean, Verdict()), "1.0.0", false, markdown: true);

        md.Should().StartWith("# Gate failed");
        md.Should().Contain("| Document | Gate | Blocking | Errors | Warnings |");
        md.Should().Contain("`clean.md`").And.Contain("`spec.md`");
    }

    [Fact]
    public void Markdown_escapes_a_pipe_inside_a_finding_message()
    {
        // A finding that quotes the document can carry a pipe (a malformed table row is nothing
        // but pipes), which would otherwise split the cell it lands in.
        var verdict = new GateVerdict(
            SourcePath: "spec.md",
            Findings: new[] { new GateFinding("tables", "tables", GateSeverity.Error, 4, "row `| a | b |` is short") },
            FailOn: GateSeverity.Error,
            BlockingCount: 1,
            SkippedChecks: System.Array.Empty<string>(),
            SuppressedCount: 0,
            ChecklistTotal: 0,
            ChecklistDone: 0,
            Metadata: System.Array.Empty<KeyValuePair<string, string>>(),
            AppliedGrades: System.Array.Empty<string>());

        var md = GateExporter.Build(Batch(verdict), "1.0.0", json: false, markdown: true);

        // Every table row still has exactly the four cells the header declares.
        foreach (var row in md.Split('\n').Where(l => l.StartsWith("| ") && !l.StartsWith("| ---")))
            row.Replace("\\|", "").Count(c => c == '|').Should().Be(5, row);
    }

    [Fact]
    public void Markdown_lists_the_documents_metadata()
    {
        const string content = "---\nworkflow: spec-writer\n---\n\n# T\n\nTODO: decide.\n";
        var md = GateExporter.Build(Batch(Verdict(content)), "1.0.0", false, true);

        md.Should().Contain("- `workflow`: spec-writer");
    }

    [Fact]
    public void Markdown_says_so_when_there_is_nothing_to_report()
    {
        var md = GateExporter.Build(
            Batch(Verdict("# Title\n\nA signed token is issued.\n")), "1.0.0", false, true);

        md.Should().Contain("# Gate passed").And.Contain("No findings.");
    }
}
