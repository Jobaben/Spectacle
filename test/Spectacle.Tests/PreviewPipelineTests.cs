using System;
using System.Collections.Generic;
using FluentAssertions;
using Spectacle.Documents;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class PreviewPipelineTests
{
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

    [Fact]
    public void Renders_initial_document_immediately()
    {
        var doc = new StubDocument();
        doc.Update("# hello");
        var sink = new StubSink();

        using var pipeline = new PreviewPipeline(doc, sink, PreviewTheme.Dark);
        pipeline.Start();

        sink.Pushed.Should().HaveCount(1);
        sink.Pushed[0].Should().Contain("<h1");
    }

    [Fact]
    public void Re_renders_on_Changed()
    {
        var doc = new StubDocument();
        doc.Update("# a");
        var sink = new StubSink();
        using var pipeline = new PreviewPipeline(doc, sink, PreviewTheme.Dark);
        pipeline.Start();

        doc.Update("# b");

        sink.Pushed.Should().HaveCount(2);
        sink.Pushed[1].Should().Contain("# b".Replace("# ", ""));
    }

    [Fact]
    public void SetTheme_re_renders()
    {
        var doc = new StubDocument();
        doc.Update("# a");
        var sink = new StubSink();
        using var pipeline = new PreviewPipeline(doc, sink, PreviewTheme.Dark);
        pipeline.Start();

        pipeline.SetTheme(PreviewTheme.HighContrast);

        sink.Pushed.Should().HaveCount(2);
        sink.Pushed[1].Should().Contain("#ffff00");
    }
}
