using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Spectacle.Annotations;

namespace Spectacle.Render;

public enum PreviewTheme { Dark, HighContrast }

public static class PreviewHtml
{
    private static readonly Lazy<string> PreviewCss = new(() => LoadAsset("preview.css"));
    private static readonly Lazy<string> DarkCss = new(() => LoadAsset("dark.css"));
    private static readonly Lazy<string> HcCss = new(() => LoadAsset("hc.css"));
    private static readonly Lazy<string> PrismCss = new(() => LoadAsset("prism.css"));
    private static readonly Lazy<string> PrismJs = new(() => LoadAsset("prism.min.js"));
    private static readonly Lazy<string> AnnotationsCss = new(() => LoadAsset("preview-annotations.css"));
    private static readonly Lazy<string> AnnotationsJs = new(() => LoadAsset("preview-annotations.js"));
    private static readonly Lazy<string> KeynavCss = new(() => LoadAsset("preview-keynav.css"));
    private static readonly Lazy<string> KeynavJs = new(() => LoadAsset("preview-keynav.js"));
    private static readonly Lazy<string> FindCss = new(() => LoadAsset("preview-find.css"));
    private static readonly Lazy<string> FindJs = new(() => LoadAsset("preview-find.js"));
    private static readonly Lazy<string> OutlineCss = new(() => LoadAsset("preview-outline.css"));
    private static readonly Lazy<string> OutlineJs = new(() => LoadAsset("preview-outline.js"));

    private static readonly JsonSerializerOptions PayloadOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Build(string bodyHtml, string baseHref, PreviewTheme theme) =>
        Build(bodyHtml, baseHref, theme, matchResult: null, outline: null);

    public static string Build(
        string bodyHtml, string baseHref, PreviewTheme theme, MatchResult? matchResult) =>
        Build(bodyHtml, baseHref, theme, matchResult, outline: null);

    public static string Build(
        string bodyHtml, string baseHref, PreviewTheme theme, MatchResult? matchResult,
        IReadOnlyList<OutlineEntry>? outline)
    {
        var themeCss = theme == PreviewTheme.HighContrast ? HcCss.Value : DarkCss.Value;
        var payloadJson = BuildPayload(matchResult);
        var outlineJson = BuildOutlinePayload(outline);

        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width,initial-scale=1" />
              <base href="{{baseHref}}" />
              <style>{{themeCss}}</style>
              <style>{{PreviewCss.Value}}</style>
              <style>{{PrismCss.Value}}</style>
              <style>{{AnnotationsCss.Value}}</style>
              <style>{{KeynavCss.Value}}</style>
              <style>{{FindCss.Value}}</style>
              <style>{{OutlineCss.Value}}</style>
            </head>
            <body>
              <main role="main">
            {{bodyHtml}}
              </main>
              <script>{{PrismJs.Value}}</script>
              <script>window.__spectacleAnnotations__ = {{payloadJson}};</script>
              <script>window.__spectacleOutline__ = {{outlineJson}};</script>
              <script>{{AnnotationsJs.Value}}</script>
              <script>{{KeynavJs.Value}}</script>
              <script>{{FindJs.Value}}</script>
              <script>{{OutlineJs.Value}}</script>
            </body>
            </html>
            """;
    }

    private static string BuildOutlinePayload(IReadOnlyList<OutlineEntry>? outline)
    {
        var entries = (outline ?? Array.Empty<OutlineEntry>()).Select(e => new
        {
            level = e.Level,
            text = e.Text,
            id = e.Id,
            line = e.Line
        });

        // Same `</` -> `<\/` guard as the annotations payload: a heading whose text
        // contains `</script>` would otherwise terminate this inline <script> early.
        return JsonSerializer.Serialize(entries, PayloadOpts).Replace("</", "<\\/");
    }

    private static string BuildPayload(MatchResult? matchResult)
    {
        // NOTE: payload is injected inline as `window.__spectacleAnnotations__ = <json>;`
        // inside a <script> tag. A user-supplied comment body containing `</script>`
        // would terminate the tag early. Mitigation: escape `</` to `<\/` after
        // serialization — the browser no longer sees a closing tag, while JSON
        // parses `\/` back to `/` so the JS side reads the original string.
        if (matchResult is null)
        {
            return JsonSerializer.Serialize(
                new { comments = Array.Empty<object>(), orphaned = Array.Empty<object>() },
                PayloadOpts).Replace("</", "<\\/");
        }

        var comments = matchResult.Matched.Select(m => new
        {
            id = m.Comment.Id,
            body = m.Comment.Body,
            originalText = m.Comment.OriginalText,
            createdAt = m.Comment.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            resolvedAt = m.Comment.ResolvedAt?.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            blockAnchor = new
            {
                kind = m.Comment.BlockAnchor.Kind,
                line = m.CurrentBlock.Line,
                textHash = m.Comment.BlockAnchor.TextHash,
                occurrenceIndex = m.Comment.BlockAnchor.OccurrenceIndex,
                leadingText = m.Comment.BlockAnchor.LeadingText,
                blockIdAtRender = m.CurrentBlock.BlockId
            }
        });

        var orphans = matchResult.Orphaned.Select(c => new
        {
            id = c.Id,
            body = c.Body,
            blockAnchor = new
            {
                kind = c.BlockAnchor.Kind,
                line = c.BlockAnchor.Line,
                leadingText = c.BlockAnchor.LeadingText
            }
        });

        return JsonSerializer.Serialize(new { comments, orphaned = orphans }, PayloadOpts)
            .Replace("</", "<\\/");
    }

    internal static string LoadAsset(string name)
    {
        var asm = typeof(PreviewHtml).Assembly;
        var resource = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("." + name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Embedded asset '{name}' not found.");
        using var s = asm.GetManifestResourceStream(resource)!;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}
