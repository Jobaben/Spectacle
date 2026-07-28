using Markdig;
using Markdig.Syntax;

namespace Spectacle.Render;

public sealed class MdRenderer
{
    // UseYamlFrontMatter is what makes an AI workflow's metadata header render as metadata.
    // Without it CommonMark reads `title: Draft` followed by the closing `---` as a *setext
    // heading*, so the header silently becomes the document's first h2 — which then shows up in
    // the outline, the heading hierarchy, and the table-of-contents check on essentially every
    // generated document. Every checker's pipeline enables it for the same reason.
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseYamlFrontMatter()
        .UseEmojiAndSmiley()
        .UseAutoIdentifiers()
        .UseGenericAttributes()
        // Last, so it replaces whatever code-block renderer the extensions above installed.
        .UseMermaid()
        .Build();

    public RenderResult Render(string markdown)
    {
        var source = markdown ?? string.Empty;
        var document = Markdown.Parse(source, _pipeline);
        var blocks = BlockTagger.TagDocument(document, source);
        var outline = OutlineExtractor.Extract(document);
        var html = document.ToHtml(_pipeline);
        return new RenderResult(html, blocks, outline);
    }

    public string ToHtml(string markdown) => Render(markdown).Html;
}
