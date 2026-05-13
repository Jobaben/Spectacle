using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Spectacle.Render;

public enum PreviewTheme { Dark, HighContrast }

public static class PreviewHtml
{
    private static readonly Lazy<string> PreviewCss = new(() => LoadAsset("preview.css"));
    private static readonly Lazy<string> DarkCss = new(() => LoadAsset("dark.css"));
    private static readonly Lazy<string> HcCss = new(() => LoadAsset("hc.css"));
    private static readonly Lazy<string> PrismCss = new(() => LoadAsset("prism.css"));
    private static readonly Lazy<string> PrismJs = new(() => LoadAsset("prism.min.js"));

    public static string Build(string bodyHtml, string baseHref, PreviewTheme theme)
    {
        var themeCss = theme == PreviewTheme.HighContrast ? HcCss.Value : DarkCss.Value;
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
            </head>
            <body>
              <main role="main">
            {{bodyHtml}}
              </main>
              <script>{{PrismJs.Value}}</script>
            </body>
            </html>
            """;
    }

    private static string LoadAsset(string name)
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
