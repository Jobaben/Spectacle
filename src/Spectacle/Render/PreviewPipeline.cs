using System;
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
    private PreviewTheme _theme;
    private bool _started;

    public PreviewPipeline(Document document, IPreviewSink sink, PreviewTheme theme)
    {
        _document = document;
        _sink = sink;
        _theme = theme;
    }

    public void Start()
    {
        if (_started) return;
        _started = true;
        _document.Changed += OnDocumentChanged;
        Render();
    }

    public void SetTheme(PreviewTheme theme)
    {
        _theme = theme;
        if (_started) Render();
    }

    private void OnDocumentChanged(object? sender, EventArgs e) => Render();

    private void Render()
    {
        var body = _renderer.ToHtml(_document.Text);
        var html = PreviewHtml.Build(body, $"https://{Web.WebViewHost.VirtualHost}/", _theme);
        _sink.Push(html);
    }

    public void Dispose() => _document.Changed -= OnDocumentChanged;
}
