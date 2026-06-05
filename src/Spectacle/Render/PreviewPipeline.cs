using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Spectacle.Annotations;
using Spectacle.Documents;

namespace Spectacle.Render;

public interface IPreviewSink
{
    void Push(string html);
}

public sealed class PreviewPipeline : IDisposable
{
    private readonly Document _document;
    private readonly IPreviewSink _sink;
    private readonly MdRenderer _renderer = new();
    private readonly AnnotationStore _store;
    private readonly object _sync = new();
    private PreviewTheme _theme;
    private bool _started;
    private AnnotationFile _file;
    private RenderResult? _lastRender;
    private MatchResult? _lastMatch;
    private long _renderVersion; // guarded by _sync; identifies the newest render

    public event EventHandler? Rendered;

    public PreviewPipeline(Document document, IPreviewSink sink, PreviewTheme theme, AnnotationStore store)
    {
        _document = document;
        _sink = sink;
        _theme = theme;
        _store = store;
        _file = _store.Load();
    }

    public void Start()
    {
        (string Html, long Version)? render = null;
        lock (_sync)
        {
            if (_started) return;
            _started = true;
            _document.Changed += OnDocumentChanged;
            render = RenderLocked();
        }
        Publish(render);
    }

    public void SetTheme(PreviewTheme theme)
    {
        (string Html, long Version)? render = null;
        lock (_sync)
        {
            _theme = theme;
            if (_started) render = RenderLocked();
        }
        Publish(render);
    }

    public IReadOnlyList<MatchedComment> SnapshotMatched()
    {
        lock (_sync)
        {
            return _lastMatch?.Matched ?? Array.Empty<MatchedComment>();
        }
    }

    public IReadOnlyList<Comment> SnapshotOrphans()
    {
        lock (_sync)
        {
            return _lastMatch?.Orphaned ?? Array.Empty<Comment>();
        }
    }

    public void HandleHostMessage(string json)
    {
        (string Html, long Version)? render = null;
        lock (_sync)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var typeEl)) return;
                var type = typeEl.GetString();

                switch (type)
                {
                    case "commentSave":    OnCommentSave(root); break;
                    case "commentDelete":  OnCommentDelete(root); break;
                    case "commentResolve": OnCommentResolve(root); break;
                    case "orphanReanchor": OnOrphanReanchor(root); break;
                    default: return;
                }
                Persist();
                render = RenderLocked();
            }
            catch (Exception ex) when (ex is JsonException || ex is KeyNotFoundException || ex is InvalidOperationException)
            {
                Console.Error.WriteLine($"[PreviewPipeline] Malformed host message; ignored: {ex.Message}. Payload: {Truncate(json, 200)}");
            }
        }
        Publish(render);
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s.Substring(0, max) + "…";

    private void OnCommentSave(JsonElement root)
    {
        var commentId = root.GetProperty("commentId").GetString()!;
        var blockId = root.GetProperty("blockId").GetString()!;
        var body = root.GetProperty("body").GetString()!;

        var block = (_lastRender?.Blocks ?? Array.Empty<TaggedBlock>())
            .FirstOrDefault(b => b.BlockId == blockId);
        if (block is null) return;

        var anchor = AnchorFromBlock(block);

        var existing = _file.Comments.FirstOrDefault(c => c.Id == commentId);
        Comment updated;
        if (existing is not null)
        {
            updated = existing with { Body = body, BlockAnchor = anchor, OriginalText = block.OriginalText };
            _file = _file with { Comments = _file.Comments.Select(c => c.Id == commentId ? updated : c).ToArray() };
        }
        else
        {
            updated = new Comment(
                Id: commentId,
                BlockAnchor: anchor,
                OriginalText: block.OriginalText,
                Body: body,
                CreatedAt: DateTime.UtcNow,
                ResolvedAt: null);
            _file = _file with { Comments = _file.Comments.Concat(new[] { updated }).ToArray() };
        }
    }

    private void OnCommentDelete(JsonElement root)
    {
        var commentId = root.GetProperty("commentId").GetString()!;
        _file = _file with { Comments = _file.Comments.Where(c => c.Id != commentId).ToArray() };
    }

    private void OnCommentResolve(JsonElement root)
    {
        var commentId = root.GetProperty("commentId").GetString()!;
        var resolved = root.GetProperty("resolved").GetBoolean();
        _file = _file with
        {
            Comments = _file.Comments.Select(c =>
                c.Id == commentId ? c with { ResolvedAt = resolved ? DateTime.UtcNow : null } : c
            ).ToArray()
        };
    }

    private void OnOrphanReanchor(JsonElement root)
    {
        var commentId = root.GetProperty("commentId").GetString()!;
        var blockId = root.GetProperty("blockId").GetString()!;
        var block = (_lastRender?.Blocks ?? Array.Empty<TaggedBlock>())
            .FirstOrDefault(b => b.BlockId == blockId);
        if (block is null) return;

        var newAnchor = AnchorFromBlock(block);

        _file = _file with
        {
            Comments = _file.Comments.Select(c =>
                c.Id == commentId ? c with { BlockAnchor = newAnchor, OriginalText = block.OriginalText } : c
            ).ToArray()
        };
    }

    private static BlockAnchor AnchorFromBlock(TaggedBlock block)
    {
        var firstLine = block.OriginalText.Split('\n')[0];
        var leading = firstLine.Length > 80 ? firstLine.Substring(0, 80) : firstLine;
        return new BlockAnchor(
            Kind: block.Kind,
            Line: block.Line,
            TextHash: block.TextHash,
            OccurrenceIndex: block.OccurrenceIndex,
            LeadingText: leading);
    }

    private void Persist() => _store.Save(_file);

    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        (string Html, long Version)? render;
        lock (_sync)
        {
            _file = _store.Load();
            render = RenderLocked();
        }
        Publish(render);
    }

    // Computes the render under _sync; publishing happens outside the lock.
    private (string Html, long Version) RenderLocked()
    {
        _lastRender = _renderer.Render(_document.Text);
        _lastMatch = AnnotationMatcher.Match(_lastRender.Blocks, _file.Comments);
        var html = PreviewHtml.Build(
            _lastRender.Html,
            $"https://{Web.WebViewHost.VirtualHost}/",
            _theme,
            _lastMatch);
        return (html, ++_renderVersion);
    }

    // Push and Rendered run with NO lock held: the file watcher raises Changed
    // on a timer thread, and Rendered handlers marshal to the UI thread
    // (Dispatcher.Invoke) and call back into Snapshot* — doing that under _sync
    // deadlocks the UI thread against the watcher thread. The version check
    // drops a render that lost the race to a newer one, so a slow thread can't
    // publish stale content over a fresher render.
    private void Publish((string Html, long Version)? render)
    {
        if (render is null) return;
        var (html, version) = render.Value;
        lock (_sync)
        {
            if (version != _renderVersion) return;
        }
        _sink.Push(html);
        Rendered?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        lock (_sync) { _document.Changed -= OnDocumentChanged; }
    }
}
