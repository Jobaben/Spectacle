using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using Spectacle.Ai;
using Spectacle.Annotations;
using Spectacle.Documents;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

/// <summary>
/// How the hands-free revision flows through the pipeline: the Claude state the preview is told
/// about, and the <c>claudeRevise</c> message coming back — which must hand out the same triaged
/// brief the clipboard path copies, and only when a CLI exists and no run is in flight.
/// </summary>
public class ClaudeRevisionPipelineTests : IDisposable
{
    private readonly string _root;

    public ClaudeRevisionPipelineTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"spectacle-claude-pipe-{Guid.NewGuid():N}");
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

    private PreviewPipeline Open(string content, RecordingSink sink)
    {
        var path = Path.Combine(_root, "draft.md");
        File.WriteAllText(path, content);
        return new PreviewPipeline(
            FileDocument.Open(path), sink, PreviewTheme.Dark, new AnnotationStore(path, _root));
    }

    private static JsonElement ClaudePayload(string html)
    {
        const string marker = "window.__spectacleGate__ = ";
        var start = html.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = html.IndexOf(";</script>", start, StringComparison.Ordinal);
        return JsonDocument.Parse(html[start..end].Replace("<\\/", "</"))
            .RootElement.GetProperty("claude");
    }

    private const string Failing = "# Auth\n\nCertainly! Here is the updated design.\n";

    [Fact]
    public void Without_a_cli_the_payload_says_so_and_the_overlay_offers_only_the_clipboard()
    {
        var sink = new RecordingSink();
        using var pipeline = Open(Failing, sink);
        pipeline.Start();

        ClaudePayload(sink.Last).ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void Claude_state_set_before_start_rides_the_first_render()
    {
        var sink = new RecordingSink();
        using var pipeline = Open(Failing, sink);
        pipeline.SetClaudeStatus(ClaudeRevisionStatus.Idle);
        pipeline.Start();

        sink.Count.Should().Be(1, "a status set before Start must not publish anything by itself");
        var claude = ClaudePayload(sink.Last);
        claude.GetProperty("available").GetBoolean().Should().BeTrue();
        claude.GetProperty("state").GetString().Should().Be("idle");
    }

    [Fact]
    public void A_status_change_re_renders_so_the_chip_tracks_the_run()
    {
        var sink = new RecordingSink();
        using var pipeline = Open(Failing, sink);
        pipeline.SetClaudeStatus(ClaudeRevisionStatus.Idle);
        pipeline.Start();

        pipeline.SetClaudeStatus(ClaudeRevisionStatus.Running);
        ClaudePayload(sink.Last).GetProperty("state").GetString().Should().Be("running");

        pipeline.SetClaudeStatus(ClaudeRevisionStatus.Failed("credit balance too low"));
        var claude = ClaudePayload(sink.Last);
        claude.GetProperty("state").GetString().Should().Be("failed");
        claude.GetProperty("detail").GetString().Should().Be("credit balance too low");
    }

    [Fact]
    public void ClaudeRevise_hands_out_the_same_triaged_brief_the_clipboard_gets()
    {
        var sink = new RecordingSink();
        using var pipeline = Open(Failing, sink);
        pipeline.SetClaudeStatus(ClaudeRevisionStatus.Idle);
        pipeline.Start();

        string? copied = null;
        string? handed = null;
        pipeline.CopyTextRequested += (_, text) => copied = text;
        pipeline.ClaudeReviseRequested += (_, text) => handed = text;

        pipeline.HandleHostMessage("""{"type":"copyFixBrief"}""");
        pipeline.HandleHostMessage("""{"type":"claudeRevise"}""");

        copied.Should().NotBeNull();
        handed.Should().NotBeNull();
        handed.Should().Be(copied!, "the runner and the clipboard must carry the same brief");
        handed.Should().Contain("Revision brief").And.Contain("ai-artifacts");
    }

    [Fact]
    public void ClaudeRevise_respects_the_readers_waives()
    {
        var sink = new RecordingSink();
        using var pipeline = Open(Failing, sink);
        pipeline.SetClaudeStatus(ClaudeRevisionStatus.Idle);
        pipeline.Start();

        // Waive the assistant-voice finding by its line-insensitive key, as the page does.
        var findings = JsonDocument.Parse(
                ExtractGateJson(sink.Last)).RootElement.GetProperty("findings");
        string? key = null;
        foreach (var f in findings.EnumerateArray())
            if (f.GetProperty("rule").GetString() == "ai-artifacts/assistant-voice")
                key = f.GetProperty("key").GetString();
        key.Should().NotBeNull();

        pipeline.HandleHostMessage($$"""{"type":"gateWaive","key":{{JsonSerializer.Serialize(key)}},"waived":true}""");

        string? handed = null;
        pipeline.ClaudeReviseRequested += (_, text) => handed = text;
        pipeline.HandleHostMessage("""{"type":"claudeRevise"}""");

        handed.Should().NotBeNull();
        handed.Should().Contain("Revision brief");
        handed.Should().NotContain("assistant-voice", "a waived finding must not reach Claude");
    }

    [Fact]
    public void ClaudeRevise_is_ignored_without_a_cli_and_while_a_run_is_in_flight()
    {
        var sink = new RecordingSink();
        using var pipeline = Open(Failing, sink);
        pipeline.Start();

        var handed = 0;
        pipeline.ClaudeReviseRequested += (_, _) => handed++;

        // No CLI: the message is a no-op, wherever it came from.
        pipeline.HandleHostMessage("""{"type":"claudeRevise"}""");
        handed.Should().Be(0);

        // Mid-run: a second request would carry a brief computed against a document the current
        // run is still rewriting.
        pipeline.SetClaudeStatus(ClaudeRevisionStatus.Running);
        pipeline.HandleHostMessage("""{"type":"claudeRevise"}""");
        handed.Should().Be(0);

        pipeline.SetClaudeStatus(ClaudeRevisionStatus.Idle);
        pipeline.HandleHostMessage("""{"type":"claudeRevise"}""");
        handed.Should().Be(1);
    }

    private static string ExtractGateJson(string html)
    {
        const string marker = "window.__spectacleGate__ = ";
        var start = html.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = html.IndexOf(";</script>", start, StringComparison.Ordinal);
        return html[start..end].Replace("<\\/", "</");
    }

    private static JsonElement LoopPayload(string html)
    {
        const string marker = "window.__spectacleLoop__ = ";
        var start = html.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = html.IndexOf(";</script>", start, StringComparison.Ordinal);
        return JsonDocument.Parse(html[start..end].Replace("<\\/", "</")).RootElement;
    }

    // ---------- the run's account of itself, on the timeline ----------

    [Fact]
    public void Stream_progress_rides_the_running_chip()
    {
        var sink = new RecordingSink();
        using var pipeline = Open(Failing, sink);
        pipeline.SetClaudeStatus(ClaudeRevisionStatus.Idle);
        pipeline.Start();

        pipeline.OnClaudeRunStarted();
        ClaudePayload(sink.Last).GetProperty("state").GetString().Should().Be("running");

        pipeline.OnClaudeRunProgress(new ClaudeRunProgress(3, 2));
        var claude = ClaudePayload(sink.Last);
        claude.GetProperty("state").GetString().Should().Be("running");
        claude.GetProperty("detail").GetString().Should().Be("turn 3 · 2 edits");
    }

    [Fact]
    public void A_launch_that_fell_back_to_user_scope_says_so_on_the_chip()
    {
        // Which project scope a run got is not visible anywhere else, and a run revising a managed
        // artifact without its project's policy loaded is worth knowing about while it happens.
        var sink = new RecordingSink();
        using var pipeline = Open(Failing, sink);
        pipeline.SetClaudeStatus(ClaudeRevisionStatus.Idle);
        pipeline.Start();

        pipeline.OnClaudeRunStarted("user scope only — no project root for this artifact");

        var claude = ClaudePayload(sink.Last);
        claude.GetProperty("state").GetString().Should().Be("running");
        claude.GetProperty("detail").GetString()
            .Should().Be("user scope only — no project root for this artifact");
    }

    [Fact]
    public void The_run_s_own_progress_replaces_the_launch_note()
    {
        // The note answers "what scope did this get"; once the stream reports work, what the run
        // is *doing* is the more useful thing for the chip to carry.
        var sink = new RecordingSink();
        using var pipeline = Open(Failing, sink);
        pipeline.SetClaudeStatus(ClaudeRevisionStatus.Idle);
        pipeline.Start();

        pipeline.OnClaudeRunStarted("user scope only — no project root");
        pipeline.OnClaudeRunProgress(new ClaudeRunProgress(1, 1));

        ClaudePayload(sink.Last).GetProperty("detail").GetString().Should().Be("turn 1 · 1 edit");
    }

    [Fact]
    public void A_run_that_saved_nothing_becomes_a_visible_timeline_entry_not_silence()
    {
        var sink = new RecordingSink();
        using var pipeline = Open(Failing, sink);
        pipeline.SetClaudeStatus(ClaudeRevisionStatus.Idle);
        pipeline.Start();

        pipeline.OnClaudeRunStarted();
        pipeline.OnClaudeRunCompleted(new ClaudeRunResult(
            false, 1, "credit balance too low", "",
            new ClaudeRunStats(Turns: 5, Edits: 0, DurationMs: 900, CostUsd: null, SessionId: "s-1")));

        var run = LoopPayload(sink.Last).GetProperty("runs")[0];
        run.GetProperty("ok").GetBoolean().Should().BeFalse();
        run.GetProperty("afterIteration").GetInt32().Should().Be(1, "the run started after the opening render");
        run.GetProperty("iterations").GetInt32().Should().Be(0, "no save landed");
        run.GetProperty("message").GetString().Should().Be("credit balance too low");
        run.GetProperty("turns").GetInt32().Should().Be(5);

        ClaudePayload(sink.Last).GetProperty("state").GetString().Should().Be("failed");
    }

    [Fact]
    public void A_finished_run_carries_the_agents_closing_message()
    {
        var sink = new RecordingSink();
        using var pipeline = Open(Failing, sink);
        pipeline.SetClaudeStatus(ClaudeRevisionStatus.Idle);
        pipeline.Start();

        pipeline.OnClaudeRunStarted();
        pipeline.OnClaudeRunCompleted(new ClaudeRunResult(
            true, 0, "", "Rewrote the intro as the brief asked.",
            new ClaudeRunStats(Turns: 4, Edits: 2, DurationMs: 41250, CostUsd: 0.07, SessionId: "s-1")));

        var run = LoopPayload(sink.Last).GetProperty("runs")[0];
        run.GetProperty("ok").GetBoolean().Should().BeTrue();
        run.GetProperty("message").GetString().Should().Be("Rewrote the intro as the brief asked.");
        run.GetProperty("edits").GetInt32().Should().Be(2);
        ClaudePayload(sink.Last).GetProperty("state").GetString().Should().Be("done");
    }
}
