using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Spectacle.Ai;
using Spectacle.Annotations;
using Spectacle.Documents;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

/// <summary>
/// The collapsed-panel revision keys, host-side: <c>copyCommentBrief</c> must hand out the brief
/// built from the unresolved comments only, <c>claudeReviseComments</c> must carry the same text
/// and honour the same CLI/run gates as the findings hand-off, and neither may ever emit an empty
/// brief — no unresolved comments means no event at all.
/// </summary>
public class CommentBriefPipelineTests : IDisposable
{
    private readonly string _root;

    public CommentBriefPipelineTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"spectacle-comment-brief-{Guid.NewGuid():N}");
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
        public string Last { get { lock (_sync) { return _pushed[^1]; } } }
    }

    private const string Draft =
        "# Auth\n\nThe capture flow is simple.\n\nCaptures expire after a while.\n";

    private PreviewPipeline Open(RecordingSink sink)
    {
        var path = Path.Combine(_root, "draft.md");
        File.WriteAllText(path, Draft);
        return new PreviewPipeline(
            FileDocument.Open(path), sink, PreviewTheme.Dark, new AnnotationStore(path, _root));
    }

    // Draft's blocks as the tagger ids them: b0 the heading, b1 and b2 the two paragraphs.
    private static void Comment(PreviewPipeline p, string id, string blockId, string body) =>
        p.HandleHostMessage(
            $$"""{"type":"commentSave","commentId":"{{id}}","blockId":"{{blockId}}","body":"{{body}}"}""");

    [Fact]
    public void With_no_comments_copyCommentBrief_hands_nothing_out()
    {
        var sink = new RecordingSink();
        using var pipeline = Open(sink);
        pipeline.Start();

        string? copied = null;
        pipeline.CopyTextRequested += (_, text) => copied = text;
        pipeline.HandleHostMessage("""{"type":"copyCommentBrief"}""");

        copied.Should().BeNull("an empty brief would send an agent off to revise nothing");
    }

    [Fact]
    public void The_copied_brief_carries_the_unresolved_comments_only()
    {
        var sink = new RecordingSink();
        using var pipeline = Open(sink);
        pipeline.Start();

        Comment(pipeline, "c-1", "b1", "Name the failure modes.");
        Comment(pipeline, "c-2", "b2", "State the expiry window.");
        pipeline.HandleHostMessage("""{"type":"commentResolve","commentId":"c-1","resolved":true}""");

        string? copied = null;
        pipeline.CopyTextRequested += (_, text) => copied = text;
        pipeline.HandleHostMessage("""{"type":"copyCommentBrief"}""");

        copied.Should().NotBeNull();
        copied.Should().Contain("Revision brief").And.Contain("reviewer comments");
        copied.Should().Contain("State the expiry window.");
        copied.Should().NotContain("Name the failure modes.",
            "a resolved comment is work already done");
    }

    [Fact]
    public void The_brief_quotes_blocks_verbatim_and_orders_bottom_up()
    {
        var sink = new RecordingSink();
        using var pipeline = Open(sink);
        pipeline.Start();

        Comment(pipeline, "c-1", "b1", "Name the failure modes.");
        Comment(pipeline, "c-2", "b2", "State the expiry window.");

        string? copied = null;
        pipeline.CopyTextRequested += (_, text) => copied = text;
        pipeline.HandleHostMessage("""{"type":"copyCommentBrief"}""");

        copied.Should().NotBeNull();
        copied.Should().Contain("> The capture flow is simple.");
        copied.Should().Contain("> Captures expire after a while.");
        var later = copied!.IndexOf("Captures expire after a while.", StringComparison.Ordinal);
        var earlier = copied.IndexOf("The capture flow is simple.", StringComparison.Ordinal);
        later.Should().BeLessThan(earlier,
            "the brief works from the end of the document backwards");
    }

    [Fact]
    public void ClaudeReviseComments_hands_out_the_same_brief_the_clipboard_gets()
    {
        var sink = new RecordingSink();
        using var pipeline = Open(sink);
        pipeline.SetClaudeStatus(ClaudeRevisionStatus.Idle);
        pipeline.Start();

        Comment(pipeline, "c-1", "b1", "Name the failure modes.");

        string? copied = null;
        string? handed = null;
        pipeline.CopyTextRequested += (_, text) => copied = text;
        pipeline.ClaudeReviseRequested += (_, text) => handed = text;

        pipeline.HandleHostMessage("""{"type":"copyCommentBrief"}""");
        pipeline.HandleHostMessage("""{"type":"claudeReviseComments"}""");

        handed.Should().NotBeNull();
        handed.Should().Be(copied!, "the runner and the clipboard must carry the same brief");
    }

    [Fact]
    public void ClaudeReviseComments_is_ignored_without_a_cli_mid_run_or_with_nothing_unresolved()
    {
        var sink = new RecordingSink();
        using var pipeline = Open(sink);
        pipeline.Start();

        Comment(pipeline, "c-1", "b1", "Name the failure modes.");

        var handed = 0;
        pipeline.ClaudeReviseRequested += (_, _) => handed++;

        // No CLI: the message is a no-op, wherever it came from.
        pipeline.HandleHostMessage("""{"type":"claudeReviseComments"}""");
        handed.Should().Be(0);

        // Mid-run: the brief would target a document the current run is still rewriting.
        pipeline.SetClaudeStatus(ClaudeRevisionStatus.Running);
        pipeline.HandleHostMessage("""{"type":"claudeReviseComments"}""");
        handed.Should().Be(0);

        pipeline.SetClaudeStatus(ClaudeRevisionStatus.Idle);
        pipeline.HandleHostMessage("""{"type":"claudeReviseComments"}""");
        handed.Should().Be(1);

        // Everything resolved: nothing to revise, so nothing is handed out.
        pipeline.HandleHostMessage("""{"type":"commentResolve","commentId":"c-1","resolved":true}""");
        pipeline.HandleHostMessage("""{"type":"claudeReviseComments"}""");
        handed.Should().Be(1);
    }

    [Fact]
    public void The_comment_brief_and_the_fix_brief_stay_distinct()
    {
        var sink = new RecordingSink();
        using var pipeline = Open(sink);
        pipeline.Start();

        Comment(pipeline, "c-1", "b1", "Name the failure modes.");

        var copied = new List<string>();
        pipeline.CopyTextRequested += (_, text) => copied.Add(text);

        pipeline.HandleHostMessage("""{"type":"copyCommentBrief"}""");
        pipeline.HandleHostMessage("""{"type":"copyFixBrief"}""");

        copied.Should().HaveCount(2);
        copied[0].Should().Contain("reviewer comments").And.Contain("Name the failure modes.");
        copied[1].Should().NotContain("Name the failure modes.",
            "the findings brief never carries the reviewer's comments");
    }
}
