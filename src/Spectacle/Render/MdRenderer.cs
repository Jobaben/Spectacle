using Markdig;
using Markdig.Syntax;

namespace Spectacle.Render;

public sealed class MdRenderer
{
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseEmojiAndSmiley()
        .UseAutoIdentifiers()
        .UseGenericAttributes()
        .Build();

    public RenderResult Render(string markdown)
    {
        var source = markdown ?? string.Empty;
        var document = Markdown.Parse(source, _pipeline);
        var blocks = BlockTagger.TagDocument(document, source);
        var html = document.ToHtml(_pipeline);
        return new RenderResult(html, blocks);
    }

    public string ToHtml(string markdown) => Render(markdown).Html;
}
