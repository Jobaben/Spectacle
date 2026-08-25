using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Spectacle.Annotations;
using Spectacle.Checks;
using Spectacle.Cli;
using Spectacle.Documents;
using Spectacle.Export;
using Spectacle.Gate;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

/// <summary>
/// The integration the reader actually performs: a real file on disk, opened through
/// <see cref="FileDocument"/> and its watcher, driven by <see cref="PreviewPipeline"/>, graded by
/// <see cref="LiveGate"/>, and pushed to a sink as the same HTML the WebView receives.
///
/// The browser test covers what the preview does with a verdict; this covers how a verdict gets
/// there — config discovery from the document's own directory, re-grading when an agent rewrites the
/// file under the watcher, and the claim that the badge and the <c>--gate</c> command are the same
/// statement. Everything here is the shipped code path except the window itself: the sink stands in
/// for the WebView, which is the one seam the WebView2 control provides.
/// </summary>
public class PreviewGatePipelineTests : IDisposable
{
    private readonly string _root;

    public PreviewGatePipelineTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"spectacle-gate-pipe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private sealed class RecordingSink : IPreviewSink
    {
        private readonly object _sync = new();
        private readonly List<string> _pushed = new();

        public void Push(string html) { lock (_sync) { _pushed.Add(html); } }
        public int Count { get { lock (_sync) { return _pushed.Count; } } }
        public string Last { get { lock (_sync) { return _pushed[^1]; } } }
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    private PreviewPipeline Open(string path, RecordingSink sink) =>
        new(FileDocument.Open(path), sink, PreviewTheme.Dark, new AnnotationStore(path, _root));

    // The verdict the preview injected, as the overlay script will read it.
    private static JsonElement GatePayload(string html)
    {
        const string marker = "window.__spectacleGate__ = ";
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, "the preview must always declare a gate payload");
        start += marker.Length;
        var end = html.IndexOf(";</script>", start, StringComparison.Ordinal);
        return JsonDocument.Parse(html[start..end].Replace("<\\/", "</")).RootElement;
    }

    // The watcher debounces, so a re-render is awaited rather than assumed.
    private static void WaitForPushes(RecordingSink sink, int expected, int timeoutMs = 5000)
    {
        var clock = Stopwatch.StartNew();
        while (sink.Count < expected && clock.ElapsedMilliseconds < timeoutMs) System.Threading.Thread.Sleep(25);
        sink.Count.Should().BeGreaterThanOrEqualTo(expected,
            $"the pipeline should have re-rendered within {timeoutMs}ms");
    }

    [Fact]
    public void The_first_render_carries_the_verdict_for_the_document_on_disk()
    {
        var path = Write("draft.md", "# Auth\n\nCertainly! Here is the updated design.\n");
        var sink = new RecordingSink();

        using var pipeline = Open(path, sink);
        pipeline.Start();

        sink.Count.Should().Be(1);
        var gate = GatePayload(sink.Last);
        gate.GetProperty("passed").GetBoolean().Should().BeFalse();
        gate.GetProperty("findings").EnumerateArray()
            .Select(f => f.GetProperty("rule").GetString())
            .Should().Contain("ai-artifacts/assistant-voice");
    }

    [Fact]
    public void A_clean_document_renders_a_passing_verdict()
    {
        var path = Write("clean.md", "# Auth\n\nThe service issues a signed token on login.\n");
        var sink = new RecordingSink();

        using var pipeline = Open(path, sink);
        pipeline.Start();

        var gate = GatePayload(sink.Last);
        gate.GetProperty("passed").GetBoolean().Should().BeTrue();
        gate.GetProperty("findings").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void Rewriting_the_file_re_grades_the_badge()
    {
        // The loop an AI workflow actually drives: the agent rewrites the file, and the reader's
        // verdict has to follow without anyone reopening anything.
        var path = Write("draft.md", "# Auth\n\nCertainly! Here is the updated design.\n");
        var sink = new RecordingSink();

        using var pipeline = Open(path, sink);
        pipeline.Start();
        GatePayload(sink.Last).GetProperty("passed").GetBoolean().Should().BeFalse();

        File.WriteAllText(path, "# Auth\n\nThe service issues a signed token on login.\n");
        WaitForPushes(sink, 2);

        GatePayload(sink.Last).GetProperty("passed").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void The_verdict_honours_the_projects_config()
    {
        // Config discovery has to work from the document's own directory, exactly as the CLI's does.
        Write(ConfigLocator.FileName, """
            { "requiredFrontMatter": ["stage"], "severity": { "bare-urls": "warning" } }
            """);
        var path = Write("draft.md", "---\nstage:\n---\n\n# Auth\n\nSee https://example.com for the shape.\n");
        var sink = new RecordingSink();

        using var pipeline = Open(path, sink);
        pipeline.Start();

        var gate = GatePayload(sink.Last);
        var rules = gate.GetProperty("findings").EnumerateArray()
            .Select(f => f.GetProperty("rule").GetString()).ToList();

        // The metadata template was enforced...
        rules.Should().Contain("front-matter/empty-value");
        // ...and the project's grade was applied, so the bare URL reports without blocking.
        gate.GetProperty("findings").EnumerateArray()
            .Single(f => f.GetProperty("rule").GetString() == "bare-urls/bare-url")
            .GetProperty("severity").GetString().Should().Be("warning");
    }

    [Fact]
    public void The_readers_verdict_is_the_same_statement_as_the_gate_commands()
    {
        // If these could disagree, a green badge would stop meaning a green pipeline.
        Write(ConfigLocator.FileName, """
            { "requiredFrontMatter": ["stage"], "severity": { "bare-urls": "warning" }, "failOn": "error" }
            """);
        var path = Write("draft.md",
            "---\nstage:\n---\n\n# Auth\n\nCertainly! See https://example.com and set {{ttl}}.\n");
        var sink = new RecordingSink();

        using var pipeline = Open(path, sink);
        pipeline.Start();
        var fromReader = GatePayload(sink.Last);

        // The command's path, assembled the way Program does.
        var config = ConfigLocator.Resolve(path, null);
        var content = File.ReadAllText(path);
        var report = ReviewReport.Compute(
            content,
            relative => File.Exists(Path.Combine(_root, relative)),
            config.RequiredSections,
            ReviewChecks.Resolve(Array.Empty<string>(), Array.Empty<string>(), config.DisabledChecks),
            config.RequiredFrontMatter);
        var fromCommand = GateVerdict.Compute(
            Path.GetFileName(path), report,
            GatePolicy.Create(config.Severity, config.FailOn),
            FrontMatter.Parse(content));

        fromReader.GetProperty("passed").GetBoolean().Should().Be(fromCommand.Passed);
        fromReader.GetProperty("failOn").GetString().Should().Be("error");
        fromReader.GetProperty("counts").GetProperty("blocking").GetInt32()
            .Should().Be(fromCommand.BlockingCount);
        fromReader.GetProperty("findings").EnumerateArray()
            .Select(f => $"{f.GetProperty("rule").GetString()}:{f.GetProperty("line").GetInt32()}:{f.GetProperty("severity").GetString()}")
            .Should().Equal(fromCommand.Findings.Select(f => $"{f.RuleId}:{f.Line}:{f.SeverityName}"));
    }

    [Fact]
    public void SnapshotVerdict_agrees_with_what_was_pushed()
    {
        var path = Write("draft.md", "# Auth\n\nTODO: decide the token lifetime.\n");
        var sink = new RecordingSink();

        using var pipeline = Open(path, sink);
        pipeline.Start();

        var snapshot = pipeline.SnapshotVerdict();
        snapshot.Should().NotBeNull();
        snapshot!.Passed.Should().BeFalse();
        GatePayload(sink.Last).GetProperty("counts").GetProperty("blocking").GetInt32()
            .Should().Be(snapshot.BlockingCount);
    }

    [Fact]
    public void Front_matter_reaches_the_preview_as_metadata_not_as_a_heading()
    {
        var path = Write("draft.md",
            "---\ntitle: Auth design\nstage: draft\n---\n\n# Auth\n\nThe token is signed.\n");
        var sink = new RecordingSink();

        using var pipeline = Open(path, sink);
        pipeline.Start();

        // Without the front-matter extension the closing --- makes this an h2 in the rendered body.
        sink.Last.Should().NotContain("<h2");
        sink.Last.Should().Contain("<h1");

        var metadata = GatePayload(sink.Last).GetProperty("metadata").EnumerateArray().ToList();
        metadata.Select(m => m.GetProperty("key").GetString()).Should().Equal("title", "stage");
        metadata.Select(m => m.GetProperty("value").GetString()).Should().Equal("Auth design", "draft");
    }

    [Fact]
    public void A_context_capsule_reaches_the_card_as_its_text_not_as_a_scalar_indicator()
    {
        var path = Write("capsule.md",
            "---\nartifact_context:\n  purpose: >-\n    Collects reviewer feedback so a later\n    session can answer it.\n  decisions:\n    - decision: Anchor answers under their question heading.\n      reason: The heading is the stable identity.\n---\n\n# Feedback\n\nText.\n");
        var sink = new RecordingSink();

        using var pipeline = Open(path, sink);
        pipeline.Start();

        var metadata = GatePayload(sink.Last).GetProperty("metadata").EnumerateArray().ToList();
        metadata.Select(m => m.GetProperty("key").GetString())
            .Should().Equal("artifact_context.purpose", "artifact_context.decisions");
        metadata[0].GetProperty("value").GetString()
            .Should().Be("Collects reviewer feedback so a later session can answer it.");
        metadata[1].GetProperty("value").GetString()
            .Should().Be("decision: Anchor answers under their question heading.; reason: The heading is the stable identity.");
    }

    [Fact]
    public void A_broken_config_degrades_the_badge_without_taking_down_the_render()
    {
        Write(ConfigLocator.FileName, "{ not json at all");
        var path = Write("draft.md", "# Auth\n\nThe token is signed.\n");
        var sink = new RecordingSink();

        using var pipeline = Open(path, sink);
        pipeline.Start();

        sink.Count.Should().Be(1);
        sink.Last.Should().Contain("<h1");
        GatePayload(sink.Last).GetProperty("passed").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Every_render_carries_a_verdict_so_the_badge_never_goes_stale()
    {
        var path = Write("draft.md", "# Auth\n\nText.\n");
        var sink = new RecordingSink();

        using var pipeline = Open(path, sink);
        pipeline.Start();
        // A theme change re-renders through the same path; the verdict has to come with it.
        pipeline.SetTheme(PreviewTheme.HighContrast);

        sink.Count.Should().Be(2);
        GatePayload(sink.Last).GetProperty("status").GetString().Should().Be("pass");
        // And the theme actually changed.
        sink.Last.Should().Contain("#ffff00");
    }

    [Fact]
    public void The_exported_html_carries_no_verdict()
    {
        // HtmlExporter is the share/archive path: a static file, no badge, no panel.
        var html = HtmlExporter.FromMarkdown("# Auth\n\nTODO: decide.\n", PreviewTheme.Dark, "auth");

        html.Should().NotContain("__spectacleGate__");
        html.Should().NotContain("sp-gate-badge");
        html.Should().Contain("<h1");
    }
}
