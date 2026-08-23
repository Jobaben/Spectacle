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
    public void A_save_that_changes_a_commented_block_reports_the_comment_addressed()
    {
        var doc = new StubDocument();
        doc.Update("# Title\n\nFirst paragraph.\n\nSecond paragraph.\n");
        var sink = new StubSink();
        using var p = NewPipeline(doc, sink);
        p.Start();

        // The reviewer comments on the first paragraph (block b1: b0 is the heading). A comment
        // save re-renders the same text, so the iteration must not move.
        p.HandleHostMessage(
            """{"type":"commentSave","commentId":"c-1","blockId":"b1","body":"Tighten this paragraph."}""");
        sink.Pushed.Last().Should().Contain("\"iteration\":1");
        sink.Pushed.Last().Should().Contain("\"commentsAddressed\":0");

        // The agent's save rewrites the commented block: iteration 2 credits it with the comment.
        doc.Update("# Title\n\nFirst paragraph, tightened.\n\nSecond paragraph.\n");

        var html = sink.Pushed.Last();
        html.Should().Contain("\"iteration\":2");
        html.Should().Contain("\"commentsAddressed\":1");
        html.Should().Contain("\"body\":\"Tighten this paragraph.\"");
        html.Should().Contain("\"commentsOpen\":0",
            "the addressed comment lost its anchor, so nothing unresolved stays matched");
    }

    [Fact]
    public void A_save_that_addresses_a_comment_resolves_it_instead_of_orphaning_it()
    {
        var doc = new StubDocument();
        doc.Update("# Title\n\nFirst paragraph.\n\nSecond paragraph.\n");
        var sink = new StubSink();
        var store = new AnnotationStore(sourcePath: Path.Combine(_root, "doc.md"), sidecarRoot: _root);
        using var p = new PreviewPipeline(doc, sink, PreviewTheme.Dark, store);
        p.Start();

        p.HandleHostMessage(
            """{"type":"commentSave","commentId":"c-1","blockId":"b1","body":"Tighten this paragraph."}""");
        doc.Update("# Title\n\nFirst paragraph, tightened.\n\nSecond paragraph.\n");

        p.SnapshotOrphans().Should().BeEmpty("an addressed comment is answered work, not a lost anchor");
        sink.Pushed.Last().Should().Contain("\"orphaned\":[]");
        store.Load().Comments.Should().ContainSingle()
            .Which.ResolvedAt.Should().NotBeNull("the resolution must survive a reload");
    }

    [Fact]
    public void A_save_that_leaves_the_commented_block_alone_keeps_the_comment_open()
    {
        var doc = new StubDocument();
        doc.Update("# Title\n\nFirst paragraph.\n\nSecond paragraph.\n");
        var sink = new StubSink();
        using var p = NewPipeline(doc, sink);
        p.Start();

        p.HandleHostMessage(
            """{"type":"commentSave","commentId":"c-1","blockId":"b2","body":"Name the failure modes."}""");
        doc.Update("# Title\n\nFirst paragraph, edited.\n\nSecond paragraph.\n");

        var html = sink.Pushed.Last();
        html.Should().Contain("\"commentsAddressed\":0",
            "the save never touched the commented block");
        html.Should().Contain("\"commentsOpen\":1");
    }

    [Fact]
    public void A_comment_resolved_between_saves_is_not_credited_to_the_next_save()
    {
        var doc = new StubDocument();
        doc.Update("# Title\n\nFirst paragraph.\n\nSecond paragraph.\n");
        var sink = new StubSink();
        using var p = NewPipeline(doc, sink);
        p.Start();

        p.HandleHostMessage(
            """{"type":"commentSave","commentId":"c-1","blockId":"b1","body":"Tighten this paragraph."}""");
        p.HandleHostMessage("""{"type":"commentResolve","commentId":"c-1","resolved":true}""");
        doc.Update("# Title\n\nFirst paragraph, rewritten anyway.\n\nSecond paragraph.\n");

        sink.Pushed.Last().Should().Contain("\"commentsAddressed\":0",
            "a comment the reviewer resolved is work already signed off");
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
