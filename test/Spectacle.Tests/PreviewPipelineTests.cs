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

public class PreviewPipelineTests : IDisposable
{
    private readonly string _root;

    public PreviewPipelineTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"spectacle-pipe-{Guid.NewGuid():N}");
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

    private PreviewPipeline NewPipeline(StubDocument doc, StubSink sink, string sourcePath = "")
    {
        var store = new AnnotationStore(
            sourcePath: string.IsNullOrEmpty(sourcePath) ? Path.Combine(_root, "doc.md") : sourcePath,
            sidecarRoot: _root);
        return new PreviewPipeline(doc, sink, PreviewTheme.Dark, store);
    }

    [Fact]
    public void Renders_initial_document_with_zero_annotations()
    {
        var doc = new StubDocument();
        doc.Update("# hello");
        var sink = new StubSink();

        using var p = NewPipeline(doc, sink);
        p.Start();

        sink.Pushed.Should().HaveCount(1);
        sink.Pushed[0].Should().Contain("<h1");
        sink.Pushed[0].Should().Contain("\"comments\":[]");
    }

    [Fact]
    public void Re_renders_on_document_change()
    {
        var doc = new StubDocument();
        doc.Update("# a");
        var sink = new StubSink();
        using var p = NewPipeline(doc, sink);
        p.Start();

        doc.Update("# b");

        sink.Pushed.Should().HaveCount(2);
    }

    [Fact]
    public void HandleHostMessage_saves_new_comment_and_refreshes()
    {
        var doc = new StubDocument();
        doc.Update("Hello world.\n");
        var sink = new StubSink();
        using var p = NewPipeline(doc, sink);
        p.Start();

        var msg = """
        {"type":"commentSave","commentId":"c-new","blockId":"b0","body":"reword"}
        """;
        p.HandleHostMessage(msg);

        sink.Pushed.Last().Should().Contain("\"c-new\"");
        sink.Pushed.Last().Should().Contain("\"reword\"");
    }

    [Fact]
    public void HandleHostMessage_deletes_comment()
    {
        var doc = new StubDocument();
        doc.Update("Hello world.\n");
        var sink = new StubSink();
        using var p = NewPipeline(doc, sink);
        p.Start();
        p.HandleHostMessage("""
        {"type":"commentSave","commentId":"c-1","blockId":"b0","body":"x"}
        """);

        p.HandleHostMessage("""
        {"type":"commentDelete","commentId":"c-1"}
        """);

        sink.Pushed.Last().Should().NotContain("\"c-1\"");
    }

    [Fact]
    public void HandleHostMessage_resolves_comment()
    {
        var doc = new StubDocument();
        doc.Update("Hello world.\n");
        var sink = new StubSink();
        using var p = NewPipeline(doc, sink);
        p.Start();
        p.HandleHostMessage("""
        {"type":"commentSave","commentId":"c-1","blockId":"b0","body":"x"}
        """);

        p.HandleHostMessage("""
        {"type":"commentResolve","commentId":"c-1","resolved":true}
        """);

        sink.Pushed.Last().Should().Contain("\"resolvedAt\":\"");
    }

    [Fact]
    public void Snapshot_returns_current_matched_comments()
    {
        var doc = new StubDocument();
        doc.Update("Hello.\n");
        var sink = new StubSink();
        using var p = NewPipeline(doc, sink);
        p.Start();
        p.HandleHostMessage("""
        {"type":"commentSave","commentId":"c-1","blockId":"b0","body":"x"}
        """);

        var snap = p.SnapshotMatched();
        snap.Should().ContainSingle().Which.Comment.Id.Should().Be("c-1");
    }

    [Fact]
    public void Comment_becomes_orphan_when_anchor_block_text_changes()
    {
        var doc = new StubDocument();
        doc.Update("Hello.\n");
        var sink = new StubSink();
        using var p = NewPipeline(doc, sink);
        p.Start();
        p.HandleHostMessage("""
        {"type":"commentSave","commentId":"c-1","blockId":"b0","body":"x"}
        """);

        doc.Update("Goodbye.\n");

        sink.Pushed.Last().Should().Contain("\"orphaned\":[")
            .And.Contain("\"c-1\"");
        sink.Pushed.Last().Should().NotContain("\"blockIdAtRender\":\"b0\",\"");
    }

    [Fact]
    public void OrphanReanchor_binds_comment_to_new_block()
    {
        var doc = new StubDocument();
        doc.Update("Hello.\n");
        var sink = new StubSink();
        using var p = NewPipeline(doc, sink);
        p.Start();
        p.HandleHostMessage("""
        {"type":"commentSave","commentId":"c-1","blockId":"b0","body":"x"}
        """);
        doc.Update("Goodbye.\n");

        p.HandleHostMessage("""
        {"type":"orphanReanchor","commentId":"c-1","blockId":"b0"}
        """);

        sink.Pushed.Last().Should().Contain("\"c-1\"");
        sink.Pushed.Last().Should().NotContain("\"orphaned\":[{\"id\":\"c-1\"");
    }

    [Fact]
    public void Reloads_sidecar_when_document_changes()
    {
        var doc = new StubDocument();
        doc.Update("Hello.\n");
        var sink = new StubSink();
        using var p = NewPipeline(doc, sink, Path.Combine(_root, "doc.md"));
        p.Start();

        // Write a comment to the sidecar out-of-band (simulating another process
        // or a manual edit).
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("Hello."));
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        var sidecarStore = new AnnotationStore(Path.Combine(_root, "doc.md"), sidecarRoot: _root);
        sidecarStore.Save(new AnnotationFile(
            FileVersion: 1,
            SourcePath: Path.Combine(_root, "doc.md"),
            SourceHashAtWrite: "",
            Comments: new[]
            {
                new Comment("c-external",
                    new BlockAnchor("paragraph", 1, hex, 0, "Hello."),
                    "Hello.", "external comment",
                    new DateTime(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc), null)
            }));

        // Trigger Document.Changed; pipeline must reload sidecar.
        doc.Update("Hello.\n");

        sink.Pushed.Last().Should().Contain("\"c-external\"");
        sink.Pushed.Last().Should().Contain("external comment");
    }

    [Fact]
    public void HandleHostMessage_swallows_malformed_json()
    {
        var doc = new StubDocument();
        doc.Update("Hello.\n");
        var sink = new StubSink();
        using var p = NewPipeline(doc, sink);
        p.Start();
        var pushedBefore = sink.Pushed.Count;

        var stderr = Console.Error;
        using var sw = new System.IO.StringWriter();
        Console.SetError(sw);
        try
        {
            // Each of these is a different shape of malformed input — should not throw.
            p.HandleHostMessage("not json at all");
            p.HandleHostMessage("{}"); // missing type
            p.HandleHostMessage("{\"type\":\"commentSave\"}"); // missing fields
            p.HandleHostMessage("{\"type\":\"unknownType\",\"x\":1}"); // unknown type (just returns)
        }
        finally { Console.SetError(stderr); }

        // None of the malformed inputs should have triggered a re-render or persist.
        sink.Pushed.Count.Should().Be(pushedBefore);
        // Stderr should contain at least one error message for the truly malformed ones.
        sw.ToString().Should().Contain("Malformed host message");
    }

    [Fact]
    public void HandleHostMessage_writes_sidecar_to_disk()
    {
        var sourcePath = Path.Combine(_root, "doc.md");
        var doc = new StubDocument();
        doc.Update("Hello.\n");
        var sink = new StubSink();
        using var p = NewPipeline(doc, sink, sourcePath);
        p.Start();

        p.HandleHostMessage("""
        {"type":"commentSave","commentId":"c-1","blockId":"b0","body":"x"}
        """);

        // Read the sidecar back via a fresh store instance.
        var store = new AnnotationStore(sourcePath, sidecarRoot: _root);
        var loaded = store.Load();
        loaded.Comments.Should().ContainSingle()
            .Which.Id.Should().Be("c-1");
    }
}
