using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Spectacle.Annotations;
using Spectacle.Gate;

namespace Spectacle.Render;

public enum PreviewTheme { Dark, Light, HighContrast }

public static class PreviewHtml
{
    private static readonly Lazy<string> PreviewCss = new(() => LoadAsset("preview.css"));
    private static readonly Lazy<string> DarkCss = new(() => LoadAsset("dark.css"));
    private static readonly Lazy<string> LightCss = new(() => LoadAsset("light.css"));
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
    private static readonly Lazy<string> GateCss = new(() => LoadAsset("preview-gate.css"));
    private static readonly Lazy<string> GateJs = new(() => LoadAsset("preview-gate.js"));
    private static readonly Lazy<string> LoopCss = new(() => LoadAsset("preview-loop.css"));
    private static readonly Lazy<string> LoopJs = new(() => LoadAsset("preview-loop.js"));

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
        IReadOnlyList<OutlineEntry>? outline) =>
        Build(bodyHtml, baseHref, theme, matchResult, outline, verdict: null);

    public static string Build(
        string bodyHtml, string baseHref, PreviewTheme theme, MatchResult? matchResult,
        IReadOnlyList<OutlineEntry>? outline, GateVerdict? verdict) =>
        Build(bodyHtml, baseHref, theme, matchResult, outline, verdict,
            loopHistory: null, waivedKeys: null);

    /// <summary>
    /// Builds the preview document. <paramref name="verdict"/> is the gate result for the open
    /// document; pass <c>null</c> for a preview with no gate overlay (the exported, static HTML
    /// takes that path). <paramref name="loopHistory"/> is the revision-session timeline and
    /// <paramref name="waivedKeys"/> the findings the reader has waived this session; both are
    /// live-reader state, so the export path passes <c>null</c> for them too.
    /// <paramref name="claude"/> is the host's Claude CLI state — availability and the current
    /// background run — and is likewise live-reader state the export path leaves <c>null</c>.
    /// </summary>
    public static string Build(
        string bodyHtml, string baseHref, PreviewTheme theme, MatchResult? matchResult,
        IReadOnlyList<OutlineEntry>? outline, GateVerdict? verdict,
        IReadOnlyList<LoopIteration>? loopHistory, IReadOnlyCollection<string>? waivedKeys,
        Spectacle.Ai.ClaudeRevisionStatus? claude = null)
    {
        var themeCss = ThemeCss(theme);
        var payloadJson = BuildPayload(matchResult);
        var outlineJson = BuildOutlinePayload(outline);
        var gateJson = BuildGatePayload(verdict, waivedKeys, claude);
        var loopJson = BuildLoopPayload(loopHistory);

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
              <style>{{GateCss.Value}}</style>
              <style>{{LoopCss.Value}}</style>
              {{MermaidAssets.HeadFor(bodyHtml)}}
            </head>
            <body>
              <main role="main">
            {{bodyHtml}}
              </main>
              <script>{{PrismJs.Value}}</script>
              <script>window.__spectacleAnnotations__ = {{payloadJson}};</script>
              <script>window.__spectacleOutline__ = {{outlineJson}};</script>
              <script>window.__spectacleGate__ = {{gateJson}};</script>
              <script>window.__spectacleLoop__ = {{loopJson}};</script>
              <script>{{AnnotationsJs.Value}}</script>
              <script>{{KeynavJs.Value}}</script>
              <script>{{FindJs.Value}}</script>
              <script>{{OutlineJs.Value}}</script>
              <script>{{GateJs.Value}}</script>
              <script>{{LoopJs.Value}}</script>
              {{MermaidAssets.BodyFor(bodyHtml, theme)}}
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

    /// <summary>
    /// Serializes the gate verdict for <c>preview-gate.js</c>, or the JSON literal <c>null</c> when
    /// no gate was computed — the script renders nothing at all in that case, so an exported HTML
    /// file carries no badge.
    /// </summary>
    private static string BuildGatePayload(
        GateVerdict? verdict, IReadOnlyCollection<string>? waivedKeys,
        Spectacle.Ai.ClaudeRevisionStatus? claude)
    {
        if (verdict is null) return "null";

        var payload = new
        {
            status = verdict.Status,
            passed = verdict.Passed,
            failOn = verdict.FailOn.ToString().ToLowerInvariant(),
            counts = new
            {
                blocking = verdict.BlockingCount,
                error = verdict.ErrorCount,
                warning = verdict.WarningCount,
                info = verdict.InfoCount,
                suppressed = verdict.SuppressedCount,
            },
            coverage = new
            {
                checksDisabled = verdict.SkippedChecks,
                suppressed = verdict.SuppressedCount,
            },
            // The session's waived finding keys, echoed back so triage state survives a re-render.
            triage = new { waived = waivedKeys ?? Array.Empty<string>() },
            // The host's Claude CLI state. `null` (or absent, for older payload consumers) means
            // no CLI: the overlay offers the clipboard hand-off only.
            claude = claude is null ? null : (object)new
            {
                available = claude.Available,
                state = claude.State,
                detail = claude.Detail,
            },
            // A list of pairs rather than an object: front-matter keys come from the document, so
            // preserving source order matters and a duplicate key must not silently vanish.
            metadata = verdict.Metadata.Select(m => new { key = m.Key, value = m.Value }),
            findings = verdict.Findings.Select(f => new
            {
                // The line-insensitive identity the waive set is keyed by.
                key = GateTriage.KeyOf(f),
                severity = f.SeverityName,
                rule = f.RuleId,
                check = f.CheckId,
                line = f.Line,
                message = f.Message,
                remedy = f.Remedy,
            }),
        };

        // A finding's message and a front-matter value both carry the document's own text into this
        // inline <script>, where a closing tag would end the script early and the rest would be
        // parsed as markup. Two things prevent it: PayloadOpts keeps the default JavaScript
        // encoder, which escapes every `<` to a \u003C sequence outright, and the `</` -> `<\/` rewrite below
        // covers the same ground for any value the encoder passes through. JSON decodes both back
        // to the original string, so the JS side reads exactly what the document said.
        return JsonSerializer.Serialize(payload, PayloadOpts).Replace("</", "<\\/");
    }

    /// <summary>
    /// Serializes the revision-loop timeline for <c>preview-loop.js</c>, or the JSON literal
    /// <c>null</c> when no session is being tracked (the export path) — the script renders nothing
    /// at all in that case. History rows carry counts only; the latest iteration additionally
    /// carries its full delta, the changed block ids, and the reviewer comments the save
    /// addressed, which is everything the HUD shows.
    /// </summary>
    private static string BuildLoopPayload(IReadOnlyList<LoopIteration>? loopHistory)
    {
        if (loopHistory is null || loopHistory.Count == 0) return "null";

        var latest = loopHistory[^1];
        var payload = new
        {
            iteration = latest.Number,
            history = loopHistory.Select(i => new
            {
                n = i.Number,
                at = i.At.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                blocking = i.Blocking,
                errors = i.Errors,
                warnings = i.Warnings,
                advisories = i.Advisories,
                @fixed = i.Delta?.Fixed.Count ?? 0,
                introduced = i.Delta?.New.Count ?? 0,
                commentsAddressed = i.CommentsAddressed.Count,
                commentsOpen = i.CommentsOpen,
            }),
            delta = latest.Delta is null ? null : new
            {
                @fixed = latest.Delta.Fixed.Select(DeltaRow),
                introduced = latest.Delta.New.Select(DeltaRow),
                persisting = latest.Delta.Persisting.Count,
            },
            // The reviewer's side of the loop: which comment blocks the latest save acted on, in
            // full like the delta. What is *still* open travels per-row in the history (and live
            // in the annotations payload), so it is not repeated here.
            comments = new
            {
                addressed = latest.CommentsAddressed.Select(c => new
                {
                    body = c.Body,
                    context = c.Context,
                    line = c.Line,
                }),
            },
            changedBlockIds = latest.ChangedBlockIds,
        };

        // Same `</` -> `<\/` guard as every other payload: finding messages quote document text.
        return JsonSerializer.Serialize(payload, PayloadOpts).Replace("</", "<\\/");

        static object DeltaRow(DeltaFinding f) =>
            new { category = f.Category, rule = f.Rule, line = f.Line, message = f.Message };
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

    /// <summary>The theme stylesheet (a <c>:root</c> custom-property block) for a theme.</summary>
    public static string ThemeCss(PreviewTheme theme) => theme switch
    {
        PreviewTheme.HighContrast => HcCss.Value,
        PreviewTheme.Light => LightCss.Value,
        _ => DarkCss.Value,
    };

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
