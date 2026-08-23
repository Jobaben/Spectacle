using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Spectacle.Annotations;
using Spectacle.Documents;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

/// <summary>
/// The pipeline end of the revision loop and the triage bench: iterations advance with the
/// document (and only with the document), waives round-trip through the payload, and the copied
/// fix brief covers exactly the unwaived findings.
/// </summary>
public class PreviewLoopPipelineTests : IDisposable
{
    private readonly string _root;

    public PreviewLoopPipelineTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"spectacle-loop-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class StubDocument : Document
    {
        private string _text = "";
        public override string Text => _text;
        public override string BaseDirectory => @"C:\";
        public void Update(string text) { _text = text; OnChanged(); }
    }

    private sealed class StubSink : IPreviewSink
    {
        public List<string> Pushed { get; } = new();
        public void Push(string html) => Pushed.Add(html);
    }

    private PreviewPipeline NewPipeline(StubDocument doc, StubSink sink)
    {
        var store = new AnnotationStore(sourcePath: Path.Combine(_root, "doc.md"), sidecarRoot: _root);
        return new PreviewPipeline(doc, sink, PreviewTheme.Dark, store);
    }

    [Fact]
    public void The_opening_render_is_iteration_one()
    {
        var doc = new StubDocument();
        doc.Update("# Title\n\nBody.\n");
        var sink = new StubSink();
        using var p = NewPipeline(doc, sink);

        p.Start();

        sink.Pushed.Last().Should().Contain("\"iteration\":1");
    }

    [Fact]
    public void A_document_change_advances_the_iteration_but_a_theme_flip_does_not()
    {
        var doc = new StubDocument();
        doc.Update("# Title\n\nStill TODO.\n");
        var sink = new StubSink();
        using var p = NewPipeline(doc, sink);
        p.Start();

        doc.Update("# Title\n\nDone now.\n");
        sink.Pushed.Last().Should().Contain("\"iteration\":2");

        p.SetTheme(PreviewTheme.Light);
        sink.Pushed.Last().Should().Contain("\"iteration\":2",
            "re-rendering unchanged text is not a revision");
    }

    [Fact]
    public void The_second_iteration_reports_what_the_revision_fixed()
    {
        var doc = new StubDocument();
        doc.Update("# Title\n\nStill TODO.\n");
        var sink = new StubSink();
        using var p = NewPipeline(doc, sink);
        p.Start();

        doc.Update("# Title\n\nDone now.\n");

        var html = sink.Pushed.Last();
        html.Should().Contain("\"fixed\":[{").And.Contain("placeholder");
        html.Should().Contain("\"changedBlockIds\":[");
    }

    [Fact]
    public void A_waive_round_trips_into_the_next_render()
    {
        var doc = new StubDocument();
        doc.Update("# Title\n\nStill TODO.\n");
        var sink = new StubSink();
        using var p = NewPipeline(doc, sink);
        p.Start();

        sink.Pushed.Last().Should().Contain("\"triage\":{\"waived\":[]}");

        p.HandleHostMessage(
            """{"type":"gateWaive","key":"lint|lint/placeholder|placeholder marker 'TODO'","waived":true}""");
        // A waive alone does not re-render — the page updated itself. The next render echoes it.
        p.SetTheme(PreviewTheme.Light);

        sink.Pushed.Last().Should().Contain("lint|lint/placeholder|placeholder marker");
    }

    [Fact]
    public void The_copied_fix_brief_covers_exactly_the_unwaived_findings()
    {
        var doc = new StubDocument();
        // Two findings: the TODO placeholder and a bare URL.
        doc.Update("# Title\n\nStill TODO.\n\nSee https://example.test/keys for details.\n");
        var sink = new StubSink();
        using var p = NewPipeline(doc, sink);
        p.Start();

        string? copied = null;
        p.CopyTextRequested += (_, text) => copied = text;

        p.HandleHostMessage(
            """{"type":"gateWaive","key":"bare-urls|bare-urls/bare-url|bare URL: https://example.test/keys","waived":true}""");
        p.HandleHostMessage("""{"type":"copyFixBrief"}""");

        copied.Should().NotBeNull();
        copied.Should().Contain("Revision brief");
        copied.Should().Contain("placeholder", "the unwaived finding stays in the brief");
        copied.Should().NotContain("bare-url", "the waived finding drops out of the brief");
    }

    [Fact]
    public void Waives_do_not_change_the_verdict_the_badge_shows()
    {
        var doc = new StubDocument();
        doc.Update("# Title\n\nStill TODO.\n");
        var sink = new StubSink();
        using var p = NewPipeline(doc, sink);
        p.Start();
        var before = p.SnapshotVerdict()!.Findings.Count;

        p.HandleHostMessage(
            """{"type":"gateWaive","key":"lint|lint/placeholder|placeholder marker 'TODO'","waived":true}""");

        p.SnapshotVerdict()!.Findings.Count.Should().Be(before,
            "waiving affects the brief, never the gate");
    }
}
