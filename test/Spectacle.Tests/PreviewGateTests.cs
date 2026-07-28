using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

/// <summary>
/// The reader's gate overlay. The verdict the preview renders is the same
/// <see cref="GateVerdict"/> the <c>--gate</c> command exits on, so "the badge is green" and "the
/// pipeline will pass" have to stay the same statement.
/// </summary>
public class PreviewGateTests
{
    private static GateVerdict Verdict(string content, GatePolicy? policy = null) =>
        GateVerdict.Compute(
            "spec.md", ReviewReport.Compute(content), policy ?? GatePolicy.Default, FrontMatter.Parse(content));

    private static string Build(GateVerdict? verdict) =>
        PreviewHtml.Build("<p>hi</p>", "https://spectacle.local/", PreviewTheme.Dark, null, null, verdict);

    // The payload is injected as `window.__spectacleGate__ = <json>;`.
    private static JsonElement Payload(string html)
    {
        const string marker = "window.__spectacleGate__ = ";
        var start = html.IndexOf(marker, System.StringComparison.Ordinal) + marker.Length;
        var end = html.IndexOf(";</script>", start, System.StringComparison.Ordinal);
        return JsonDocument.Parse(html[start..end].Replace("<\\/", "</")).RootElement;
    }

    [Fact]
    public void The_overlay_assets_are_always_inlined()
    {
        var html = Build(null);

        html.Should().Contain("#sp-gate-badge");
        html.Should().Contain("preview-gate.js");
    }

    [Fact]
    public void No_verdict_injects_a_null_payload_so_the_overlay_renders_nothing()
    {
        // The exported, static HTML takes this path: no gate was computed, so no badge is shown.
        Build(null).Should().Contain("window.__spectacleGate__ = null;");
    }

    [Fact]
    public void A_verdict_injects_its_status_threshold_and_counts()
    {
        var payload = Payload(Build(Verdict("# T\n\nTODO: decide.\n")));

        payload.GetProperty("status").GetString().Should().Be("fail");
        payload.GetProperty("passed").GetBoolean().Should().BeFalse();
        payload.GetProperty("failOn").GetString().Should().Be("error");
        payload.GetProperty("counts").GetProperty("error").GetInt32().Should().BeGreaterThan(0);
        payload.GetProperty("counts").GetProperty("blocking").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public void A_clean_document_injects_a_passing_payload()
    {
        var payload = Payload(Build(Verdict("# Title\n\nA signed token is issued.\n")));

        payload.GetProperty("passed").GetBoolean().Should().BeTrue();
        payload.GetProperty("findings").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void Findings_carry_the_line_the_rule_and_the_fix_the_panel_shows()
    {
        var findings = Payload(Build(Verdict("# T\n\nTODO: decide.\n"))).GetProperty("findings");
        var lint = findings.EnumerateArray().First(f => f.GetProperty("rule").GetString() == "lint/placeholder");

        lint.GetProperty("severity").GetString().Should().Be("error");
        lint.GetProperty("line").GetInt32().Should().Be(3);
        lint.GetProperty("check").GetString().Should().Be("lint");
        // The panel shows the same instruction --fix-brief hands the authoring agent.
        lint.GetProperty("remedy").GetString().Should().Be(RuleCatalog.RemedyOf("lint/placeholder"));
    }

    [Fact]
    public void Metadata_is_injected_as_ordered_pairs_not_an_object()
    {
        // Source order matters for a metadata card, and a duplicate key must not silently vanish.
        const string content = "---\nworkflow: spec-writer\nstage: draft\nstage: final\n---\n\n# T\n\nText.\n";
        var metadata = Payload(Build(Verdict(content))).GetProperty("metadata");

        metadata.GetArrayLength().Should().Be(3);
        metadata[0].GetProperty("key").GetString().Should().Be("workflow");
        metadata[1].GetProperty("value").GetString().Should().Be("draft");
        metadata[2].GetProperty("value").GetString().Should().Be("final");
    }

    [Fact]
    public void Coverage_is_injected_so_the_panel_can_qualify_a_green_badge()
    {
        var checks = ReviewChecks.Resolve(new[] { "lint" }, System.Array.Empty<string>(), System.Array.Empty<string>());
        var report = ReviewReport.Compute("# T\n\nText.\n", _ => true, System.Array.Empty<string>(), checks);
        var payload = Payload(Build(GateVerdict.Compute("spec.md", report, GatePolicy.Default)));

        payload.GetProperty("coverage").GetProperty("checksDisabled").GetArrayLength()
            .Should().BeGreaterThan(0);
    }

    [Fact]
    public void A_closing_script_tag_in_a_finding_cannot_break_out_of_the_payload()
    {
        // Front-matter values are echoed verbatim into the payload, so the document's own text
        // reaches an inline <script> — where a closing tag would end the script early and the rest
        // would be parsed as markup.
        const string hostile = "</script><script>alert(1)</script>";
        var html = Build(Verdict($"---\nnote: {hostile}\n---\n\n# T\n\nText.\n"));

        // The browser never sees a closing tag it could act on...
        html.Should().NotContain("alert(1)</script>");
        // ...and the value still arrives intact for the panel that renders it.
        Payload(html).GetProperty("metadata")[0].GetProperty("value").GetString()
            .Should().Be(hostile);
    }

    [Fact]
    public void Every_theme_defines_the_severity_tokens_the_overlay_reads()
    {
        foreach (var theme in new[] { PreviewTheme.Dark, PreviewTheme.HighContrast })
        {
            var html = PreviewHtml.Build("", "x", theme);
            foreach (var token in new[] { "--gate-pass:", "--gate-error:", "--gate-warning:", "--gate-info:" })
                html.Should().Contain(token, $"{theme} must define {token}");
        }
    }
}
