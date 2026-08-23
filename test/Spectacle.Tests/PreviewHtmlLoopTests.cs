using System;
using System.Collections.Generic;
using FluentAssertions;
using Spectacle.Gate;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

/// <summary>
/// The preview page's revision-loop and triage payloads: injected for the live reader, absent
/// (JSON <c>null</c>) on the export path, and guarded against document text that would close the
/// inline script tag early.
/// </summary>
public class PreviewHtmlLoopTests
{
    private static GateVerdict VerdictWith(params GateFinding[] findings)
    {
        var text = "# Title\n\nBody.\n";
        var report = ReviewReport.Compute(text);
        var computed = GateVerdict.Compute("doc.md", report, GatePolicy.Default, FrontMatter.Parse(text));
        return findings.Length == 0 ? computed : computed with { Findings = findings };
    }

    private static LoopIteration Iteration(int n, ReviewDelta? delta = null) => new(
        Number: n,
        At: new DateTime(2026, 8, 23, 12, 0, n, DateTimeKind.Utc),
        Blocking: 2, Errors: 2, Warnings: 1, Advisories: 0,
        Delta: delta,
        ChangedBlockIds: new[] { "b3", "b7" });

    [Fact]
    public void Without_a_session_the_loop_payload_is_the_null_literal()
    {
        var html = PreviewHtml.Build("<h1>x</h1>", "https://x/", PreviewTheme.Dark, null, null);

        html.Should().Contain("window.__spectacleLoop__ = null;",
            "the export path and a gate-less preview carry no loop HUD");
    }

    [Fact]
    public void The_session_history_is_injected_with_the_latest_delta_and_changed_blocks()
    {
        var delta = new ReviewDelta(
            Fixed: new[] { new DeltaFinding("lint", "placeholder", 4, "placeholder marker 'TODO'") },
            New: new[] { new DeltaFinding("bare-urls", "bare-url", 9, "https://x.example") },
            Persisting: Array.Empty<DeltaFinding>(),
            0, 0, 0, 0);
        var history = new[] { Iteration(1), Iteration(2, delta) };

        var html = PreviewHtml.Build(
            "<h1>x</h1>", "https://x/", PreviewTheme.Dark, null, null, VerdictWith(),
            history, waivedKeys: null);

        html.Should().Contain("\"iteration\":2");
        html.Should().Contain("\"changedBlockIds\":[\"b3\",\"b7\"]");
        // The default JavaScript encoder escapes apostrophes in payload strings.
        html.Should().Contain("placeholder marker \\u0027TODO\\u0027");
        html.Should().Contain("\"persisting\":0");
        // History rows carry counts, including the per-iteration delta tallies.
        html.Should().Contain("\"fixed\":1").And.Contain("\"introduced\":1");
    }

    [Fact]
    public void The_gate_payload_carries_finding_keys_and_the_waive_set()
    {
        var finding = new GateFinding(
            "ai-artifacts", "ai-artifacts/unfilled-template", GateSeverity.Error, 3, "token '{{ttl}}'");
        var verdict = VerdictWith(finding);

        var html = PreviewHtml.Build(
            "<h1>x</h1>", "https://x/", PreviewTheme.Dark, null, null, verdict,
            loopHistory: null, waivedKeys: new[] { GateTriage.KeyOf(finding) });

        html.Should().Contain("\"key\":\"ai-artifacts|ai-artifacts/unfilled-template|token \\u0027{{ttl}}\\u0027\"");
        html.Should().Contain("\"triage\":{\"waived\":[\"ai-artifacts|ai-artifacts/unfilled-template|token \\u0027{{ttl}}\\u0027\"]}");
    }

    [Fact]
    public void A_delta_message_cannot_close_the_inline_script_tag()
    {
        var delta = new ReviewDelta(
            Fixed: new[] { new DeltaFinding("lint", "placeholder", 4, "sneaky </script> text") },
            New: Array.Empty<DeltaFinding>(),
            Persisting: Array.Empty<DeltaFinding>(),
            0, 0, 0, 0);

        var html = PreviewHtml.Build(
            "<h1>x</h1>", "https://x/", PreviewTheme.Dark, null, null, VerdictWith(),
            new[] { Iteration(1), Iteration(2, delta) }, waivedKeys: null);

        html.Should().NotContain("sneaky </script>",
            "a `</` in a payload string must be escaped so the script tag survives");
    }
}
