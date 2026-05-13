using Markdig;

namespace Spectacle.Render;

public sealed class MdRenderer
{
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseEmojiAndSmiley()
        .UseAutoIdentifiers()
        .Build();

    public string ToHtml(string markdown) => Markdown.ToHtml(markdown ?? string.Empty, _pipeline);
}
