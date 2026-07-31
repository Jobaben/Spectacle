using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Spectacle.Checks;
using Spectacle.Gate;
using Xunit;

namespace Spectacle.Tests;

public class GateVerdictTests
{
    private const string Clean = "# Auth design\n\nThe service issues a signed token on login.\n";

    private static GatePolicy Graded(string key, string severity, string? failOn = null) =>
        GatePolicy.Create(new Dictionary<string, string> { [key] = severity }, failOn);

    [Fact]
    public void A_clean_document_passes()
    {
        var verdict = GateVerdict.Compute("spec.md", ReviewReport.Compute(Clean), GatePolicy.Default);

        verdict.Passed.Should().BeTrue();
        verdict.Status.Should().Be("pass");
        verdict.BlockingCount.Should().Be(0);
        verdict.Findings.Should().BeEmpty();
        verdict.CoverageReduced.Should().BeFalse();
    }

    [Fact]
    public void An_error_fails_the_gate()
    {
        var report = ReviewReport.Compute("# Title\n\nTODO: decide the token lifetime.\n");
        var verdict = GateVerdict.Compute("spec.md", report, GatePolicy.Default);

        verdict.Passed.Should().BeFalse();
        verdict.Status.Should().Be("fail");
        verdict.ErrorCount.Should().BeGreaterThan(0);
        verdict.BlockingCount.Should().Be(verdict.ErrorCount);
    }

    [Fact]
    public void A_downgraded_rule_is_still_reported_but_stops_blocking()
    {
        // The point of grading: the finding never disappears, it just stops failing the build.
        const string content = "# Title\n\nSee https://example.com for details.\n";
        var report = ReviewReport.Compute(content);

        GateVerdict.Compute("spec.md", report, GatePolicy.Default).Passed.Should().BeFalse();

        var graded = GateVerdict.Compute("spec.md", report, Graded("bare-urls", "warning"));
        graded.Passed.Should().BeTrue();
        graded.WarningCount.Should().BeGreaterThan(0);
        graded.Findings.Should().Contain(f => f.CheckId == "bare-urls");
    }

    [Fact]
    public void A_stricter_threshold_makes_warnings_block()
    {
        const string content = "# Title\n\nSee https://example.com for details.\n";
        var report = ReviewReport.Compute(content);

        var verdict = GateVerdict.Compute("spec.md", report, Graded("bare-urls", "warning", "warning"));

        verdict.Passed.Should().BeFalse();
        verdict.BlockingCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Advisories_are_carried_but_never_block()
    {
        var report = ReviewReport.Compute("# Title\n\nWe should probably cache the token, etc.\n");
        var verdict = GateVerdict.Compute("spec.md", report, GatePolicy.Default);

        verdict.InfoCount.Should().BeGreaterThan(0);
        verdict.Findings.Should().Contain(f => f.CheckId == "prose");
        verdict.Blocking.Should().NotContain(f => f.Severity == GateSeverity.Info);
    }

    [Fact]
    public void Reports_reduced_coverage_when_a_check_is_off()
    {
        var checks = ReviewChecks.Resolve(new[] { "lint" }, System.Array.Empty<string>(), System.Array.Empty<string>());
        var report = ReviewReport.Compute(Clean, _ => true, System.Array.Empty<string>(), checks);

        var verdict = GateVerdict.Compute("spec.md", report, GatePolicy.Default);

        verdict.Passed.Should().BeTrue();
        // A pass earned by running fewer checks is not the same fact as a clean pass.
        verdict.CoverageReduced.Should().BeTrue();
        verdict.SkippedChecks.Should().Contain("toc");
    }

    [Fact]
    public void Reports_reduced_coverage_when_a_finding_is_suppressed_inline()
    {
        const string content = "# Title\n\n<!-- spectacle-disable-next-line lint -->\nTODO: decide.\n";
        var verdict = GateVerdict.Compute("spec.md", ReviewReport.Compute(content), GatePolicy.Default);

        verdict.SuppressedCount.Should().BeGreaterThan(0);
        verdict.CoverageReduced.Should().BeTrue();
    }

    [Fact]
    public void Echoes_the_documents_front_matter_metadata()
    {
        const string content = "---\nworkflow: spec-writer\nstage: draft\n---\n\n# Auth\n\nText here.\n";
        var header = FrontMatter.Parse(content);

        var verdict = GateVerdict.Compute("spec.md", ReviewReport.Compute(content), GatePolicy.Default, header);

        verdict.Metadata.Select(m => m.Key).Should().Equal("workflow", "stage");
        verdict.Metadata.Select(m => m.Value).Should().Equal("spec-writer", "draft");
    }

    [Fact]
    public void Records_the_applied_grades()
    {
        var verdict = GateVerdict.Compute("spec.md", ReviewReport.Compute(Clean), Graded("bare-urls", "warning"));

        verdict.AppliedGrades.Should().Equal("bare-urls=warning");
    }

    [Fact]
    public void Groups_findings_by_severity_worst_first()
    {
        const string content = "# Title\n\nTODO: decide.\n\nWe should probably cache it.\n";
        var verdict = GateVerdict.Compute("spec.md", ReviewReport.Compute(content), GatePolicy.Default);

        verdict.BySeverity.Select(g => g.Key).Should().BeInDescendingOrder();
        verdict.BySeverity.First().Key.Should().Be(GateSeverity.Error);
    }

    [Fact]
    public void A_batch_passes_only_when_every_document_passes()
    {
        var clean = GateVerdict.Compute("a.md", ReviewReport.Compute(Clean), GatePolicy.Default);
        var dirty = GateVerdict.Compute(
            "b.md", ReviewReport.Compute("# T\n\nTODO: decide.\n"), GatePolicy.Default);

        new GateBatch(new[] { clean, clean }).Passed.Should().BeTrue();
        new GateBatch(new[] { clean, dirty }).Passed.Should().BeFalse();
        new GateBatch(new[] { clean, dirty }).Failed.Should().HaveCount(1);
        new GateBatch(new[] { clean, dirty }).Status.Should().Be("fail");
    }

    [Fact]
    public void A_batch_sums_the_per_document_counts()
    {
        var dirty = GateVerdict.Compute(
            "b.md", ReviewReport.Compute("# T\n\nTODO: decide.\n"), GatePolicy.Default);
        var batch = new GateBatch(new[] { dirty, dirty });

        batch.ErrorCount.Should().Be(dirty.ErrorCount * 2);
        batch.BlockingCount.Should().Be(dirty.BlockingCount * 2);
    }
}
