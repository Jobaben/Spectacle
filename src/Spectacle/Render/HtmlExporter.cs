using System.Net;

namespace Spectacle.Render;

/// <summary>
/// Renders a Markdown document to a single self-contained HTML file. The theme,
/// preview, and syntax-highlight CSS plus the Prism highlighter are all inlined,
/// so the exported file renders identically with no external assets and no
/// network access. Unlike the live preview it carries none of the host-driven
/// scripting (annotations, keyboard navigation, find) — the result is a static,
/// portable document suitable for sharing or archiving.
/// </summary>
public static class HtmlExporter
{
    public static string FromMarkdown(string markdown, PreviewTheme theme, string title) =>
        Build(new MdRenderer().ToHtml(markdown), theme, title);

    public static string Build(string bodyHtml, PreviewTheme theme, string title)
    {
        var themeCss = PreviewHtml.LoadAsset(theme == PreviewTheme.HighContrast ? "hc.css" : "dark.css");
        var previewCss = PreviewHtml.LoadAsset("preview.css");
        var prismCss = PreviewHtml.LoadAsset("prism.css");
        var prismJs = PreviewHtml.LoadAsset("prism.min.js");
        var safeTitle = WebUtility.HtmlEncode(title);

        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width,initial-scale=1" />
              <title>{{safeTitle}}</title>
              <style>{{themeCss}}</style>
              <style>{{previewCss}}</style>
              <style>{{prismCss}}</style>
            </head>
            <body>
              <main role="main">
            {{bodyHtml}}
              </main>
              <script>{{prismJs}}</script>
            </body>
            </html>
            """;
    }
}
