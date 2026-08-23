using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Spectacle.Gate;
using Xunit;

namespace Spectacle.Tests;

/// <summary>
/// Session triage over a verdict: waives are keyed line-insensitively, the filtered verdict
/// recomputes its tallies under the same threshold, and stale waives are pruned.
/// </summary>
public class GateTriageTests
{
    private static GateVerdict Verdict(params GateFinding[] findings)
    {
        var list = findings.ToList();
        return new GateVerdict(
            SourcePath: "doc.md",
            Findings: list,
            FailOn: GateSeverity.Error,
            BlockingCount: list.Count(f => f.Severity == GateSeverity.Error),
            SkippedChecks: new[] { "duplication" },
            SuppressedCount: 1,
            ChecklistTotal: 0,
            ChecklistDone: 0,
            Metadata: Array.Empty<KeyValuePair<string, string>>(),
            AppliedGrades: Array.Empty<string>());
    }

    private static GateFinding Error(string message, int line = 3) =>
        new("ai-artifacts", "ai-artifacts/unfilled-template", GateSeverity.Error, line, message);

    private static GateFinding Warning(string message, int line = 9) =>
        new("bare-urls", "bare-urls/bare-url", GateSeverity.Warning, line, message);

    [Fact]
    public void Key_ignores_the_line_so_a_finding_that_moved_stays_waived()
    {
        var atLine3 = Error("token '{{ttl}}'", line: 3);
        var atLine40 = Error("token '{{ttl}}'", line: 40);

        GateTriage.KeyOf(atLine3).Should().Be(GateTriage.KeyOf(atLine40));
    }

    [Fact]
    public void Without_removes_waived_findings_and_recomputes_the_tallies()
    {
        var keep = Error("token '{{ttl}}'");
        var waive = Error("token '{{region}}'");
        var verdict = Verdict(keep, waive, Warning("bare URL"));

        var triaged = GateTriage.Without(verdict, new[] { GateTriage.KeyOf(waive) });

        triaged.Findings.Should().HaveCount(2);
        triaged.Findings.Should().NotContain(f => f.Message.Contains("region"));
        triaged.BlockingCount.Should().Be(1);
        triaged.ErrorCount.Should().Be(1);
        triaged.WarningCount.Should().Be(1);
    }

    [Fact]
    public void Waiving_every_blocking_finding_makes_the_triaged_verdict_pass()
    {
        var only = Error("token '{{ttl}}'");
        var verdict = Verdict(only, Warning("bare URL"));
        verdict.Passed.Should().BeFalse();

        var triaged = GateTriage.Without(verdict, new[] { GateTriage.KeyOf(only) });

        triaged.Passed.Should().BeTrue("warnings sit under the error threshold");
    }

    [Fact]
    public void Coverage_context_is_carried_unchanged()
    {
        var waive = Error("token '{{ttl}}'");
        var triaged = GateTriage.Without(Verdict(waive), new[] { GateTriage.KeyOf(waive) });

        triaged.SkippedChecks.Should().ContainSingle().Which.Should().Be("duplication");
        triaged.SuppressedCount.Should().Be(1, "waiving reduces the brief, never the caveats");
    }

    [Fact]
    public void Unknown_keys_and_empty_sets_leave_the_verdict_alone()
    {
        var verdict = Verdict(Error("token '{{ttl}}'"));

        GateTriage.Without(verdict, Array.Empty<string>()).Should().BeSameAs(verdict);
        GateTriage.Without(verdict, new[] { "nope|nope|nope" }).Should().BeSameAs(verdict);
    }

    [Fact]
    public void Prune_drops_waives_whose_finding_is_gone()
    {
        var live = Error("token '{{ttl}}'");
        var gone = Error("token '{{region}}'");
        var verdict = Verdict(live);

        var survivors = GateTriage.Prune(verdict, new[] { GateTriage.KeyOf(live), GateTriage.KeyOf(gone) });

        survivors.Should().ContainSingle().Which.Should().Be(GateTriage.KeyOf(live));
    }
}
