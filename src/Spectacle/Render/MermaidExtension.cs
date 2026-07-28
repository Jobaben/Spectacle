using System;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace Spectacle.Render;

/// <summary>
/// Renders a <c>```mermaid</c> fence as a diagram container instead of a code block, so
/// <c>preview-mermaid.js</c> can draw it. Every other fence keeps the stock Markdig rendering —
/// this extension replaces the code-block renderer and delegates to the base implementation for
/// anything that is not a mermaid fence.
///
/// The container deliberately keeps the diagram's source inside a <c>&lt;pre&gt;&lt;code&gt;</c>:
/// that is what the document renders as when the script does not run (a stripped-down host, a
/// diagram whose syntax mermaid rejects), which is exactly the readable code block Spectacle
/// showed before diagrams were supported. Rendering upgrades that fallback rather than replacing
/// it, so a diagram can fail without taking a paragraph of the document with it.
/// </summary>
public sealed class MermaidExtension : IMarkdownExtension
{
    /// <summary>The single instance; the extension holds no per-pipeline state.</summary>
    public static readonly MermaidExtension Instance = new();

    private MermaidExtension() { }

    public void Setup(MarkdownPipelineBuilder pipeline) { }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        if (renderer is not HtmlRenderer html) return;

        // Take the pipeline's own code-block renderer out and put the mermaid-aware subclass in
        // its place — at the same index, which is load-bearing. Markdig models a YAML front-matter
        // header as a CodeBlock, and UseYamlFrontMatter suppresses it with a renderer of its own;
        // a code-block renderer placed ahead of that one claims the header first and renders the
        // document's metadata as a visible code block.
        //
        // Replacing by instance rather than by FindExact<T> keeps this idempotent: a second Setup
        // finds the subclass, whose base type is still CodeBlockRenderer, and swaps in one more
        // that behaves identically.
        var existing = html.ObjectRenderers.Find<CodeBlockRenderer>();
        if (existing is null)
        {
            html.ObjectRenderers.Add(new MermaidCodeBlockRenderer());
            return;
        }

        var at = html.ObjectRenderers.IndexOf(existing);
        html.ObjectRenderers.RemoveAt(at);
        html.ObjectRenderers.Insert(at, new MermaidCodeBlockRenderer());
    }
}

/// <summary>Pipeline sugar for <see cref="MermaidExtension"/>, matching Markdig's own Use* style.</summary>
public static class MermaidExtensions
{
    /// <summary>Renders <c>```mermaid</c> fences as diagrams instead of code blocks.</summary>
    public static MarkdownPipelineBuilder UseMermaid(this MarkdownPipelineBuilder pipeline)
    {
        pipeline.Extensions.AddIfNotAlready(MermaidExtension.Instance);
        return pipeline;
    }
}

/// <summary>
/// The stock code-block renderer, extended to emit a mermaid fence as a
/// <see cref="MermaidDiagram.Marker"/>-tagged <c>&lt;figure&gt;</c> wrapping the diagram source.
/// </summary>
internal sealed class MermaidCodeBlockRenderer : CodeBlockRenderer
{
    protected override void Write(HtmlRenderer renderer, CodeBlock obj)
    {
        if (obj is not FencedCodeBlock fenced || !MermaidDiagram.IsDiagramFence(fenced.Info))
        {
            base.Write(renderer, obj);
            return;
        }

        // The figure carries the block's identity (BlockTagger's md-block class, data-block-id,
        // data-line, the text hash a comment anchors to, the tabindex keyboard navigation needs),
        // so a diagram is a first-class block: focusable, commentable, and reachable from the
        // outline the same way a code block is.
        renderer.Write("<figure");
        renderer.WriteAttributes(obj);
        renderer.Write(" ").Write(MermaidDiagram.Marker).Write("=\"")
                .Write(MermaidDiagram.PendingState).Write("\"");
        renderer.WriteLine(">");

        renderer.Write("<pre class=\"").Write(MermaidDiagram.SourceClass).Write("\"><code>");
        renderer.WriteLeafRawLines(obj, writeEndOfLines: false, escape: true);
        renderer.WriteLine("</code></pre>");

        renderer.WriteLine("</figure>");
    }
}

/// <summary>
/// What "a mermaid diagram" means to the renderer, the preview, and the gate: which fences are
/// diagrams, the attribute that marks one in the emitted HTML, and the diagram keywords mermaid
/// itself recognizes.
/// </summary>
public static class MermaidDiagram
{
    /// <summary>The fence language that means "this is a diagram".</summary>
    public const string Language = "mermaid";

    /// <summary>
    /// The attribute stamped on every rendered diagram container. It is also how
    /// <see cref="IsRenderedIn"/> decides a document needs the mermaid bundle, so it must stay
    /// unique to a diagram — no other renderer emits it.
    /// </summary>
    public const string Marker = "data-mermaid";

    /// <summary>The marker's value before the script has drawn the diagram.</summary>
    public const string PendingState = "pending";

    /// <summary>Class on the <c>&lt;pre&gt;</c> holding the diagram's source text.</summary>
    public const string SourceClass = "mermaid-source";

    /// <summary>
    /// Whether a fence's info string opens a mermaid diagram. Only the language token is
    /// considered, so <c>```mermaid</c> and <c>```mermaid theme=dark</c> both count, and the
    /// comparison ignores case the way every Markdown highlighter does.
    /// </summary>
    public static bool IsDiagramFence(string? info)
    {
        if (string.IsNullOrWhiteSpace(info)) return false;
        var token = info.AsSpan().Trim();
        var space = token.IndexOfAny(' ', '\t');
        if (space >= 0) token = token.Slice(0, space);
        return token.Equals(Language, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether <paramref name="bodyHtml"/> contains a rendered diagram — the test the preview and
    /// the HTML export use to decide whether to inline the mermaid bundle. It is 3.4 MB, so a
    /// document without a diagram must not carry it: an exported file would be a thousand times
    /// the size of its own prose.
    /// </summary>
    public static bool IsRenderedIn(string? bodyHtml) =>
        bodyHtml is not null && bodyHtml.Contains(Marker + "=", StringComparison.Ordinal);

    /// <summary>
    /// The diagram-type keywords the vendored bundle accepts at the head of a diagram, in mermaid's
    /// own spelling. A diagram opening with anything else is one mermaid will refuse to draw, which
    /// is what <see cref="MermaidChecker"/> reports.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Enumerated from the vendored <c>mermaid.min.js</c> rather than from mermaid's documentation,
    /// because the two disagree: the docs describe <c>zenuml</c> and <c>usecase</c>, which this
    /// bundle does not register (zenuml ships as a separate plugin), and they name <c>radar</c>
    /// where the bundle answers only to <c>radar-beta</c>. A keyword wrongly listed here lets a
    /// diagram that cannot draw pass the gate; a keyword wrongly missing turns one that draws fine
    /// into a false finding — so <c>mermaid.browser.test.js</c> asserts this list against the
    /// bundle's own detector, and fails if a bundle bump moves either way.
    /// </para>
    /// <para>
    /// The casing is load-bearing. Mermaid's detectors are case-sensitive: it draws
    /// <c>classDiagram</c> and refuses <c>classdiagram</c>. So the spellings here are exact and
    /// <see cref="MermaidChecker"/> compares against them exactly — which is what lets it catch a
    /// keyword that is merely miscapitalized, a diagram that looks right and draws nothing.
    /// (The fence's own <c>```mermaid</c> tag is a different question, and stays case-insensitive
    /// like every other Markdown language tag — see <see cref="IsDiagramFence"/>.)
    /// </para>
    /// </remarks>
    public static readonly string[] DiagramKeywords =
    {
        "architecture-beta", "block", "block-beta", "C4Component", "C4Container", "C4Context",
        "C4Deployment", "C4Dynamic", "classDiagram", "classDiagram-v2", "erDiagram",
        "flowchart", "flowchart-elk", "flowchart-v2", "gantt", "gitGraph", "graph",
        "info", "journey", "kanban", "mindmap", "packet", "packet-beta", "pie",
        "quadrantChart", "radar-beta", "requirement", "requirementDiagram", "sankey",
        "sankey-beta", "sequenceDiagram", "stateDiagram", "stateDiagram-v2", "timeline",
        "treemap", "treemap-beta", "xychart", "xychart-beta",
    };
}
