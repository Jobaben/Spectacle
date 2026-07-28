using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class PreviewHtmlTests
{
    [Fact]
    public void Wraps_body_in_main_landmark()
    {
        var html = PreviewHtml.Build("<p>hi</p>", "https://spectacle.local/", PreviewTheme.Dark);
        html.Should().Contain("<main role=\"main\">").And.Contain("<p>hi</p>").And.Contain("</main>");
    }

    [Fact]
    public void Embeds_preview_css() =>
        PreviewHtml.Build("", "x", PreviewTheme.Dark).Should().Contain("--bg:");

    [Fact]
    public void Dark_theme_includes_dark_css() =>
        PreviewHtml.Build("", "x", PreviewTheme.Dark).Should().Contain("#1e1e1e");

    [Fact]
    public void Light_theme_includes_light_css() =>
        PreviewHtml.Build("", "x", PreviewTheme.Light).Should().Contain("#0969da");

    [Fact]
    public void Light_theme_uses_white_background() =>
        PreviewHtml.Build("", "x", PreviewTheme.Light).Should().Contain("--bg: #ffffff");

    [Fact]
    public void HighContrast_theme_includes_hc_css() =>
        PreviewHtml.Build("", "x", PreviewTheme.HighContrast).Should().Contain("#ffff00");

    [Fact]
    public void Sets_base_href()
    {
        var html = PreviewHtml.Build("", "https://spectacle.local/", PreviewTheme.Dark);
        html.Should().Contain("<base href=\"https://spectacle.local/\"");
    }

    [Fact]
    public void Includes_prism_script() =>
        PreviewHtml.Build("", "x", PreviewTheme.Dark).Should().Contain("Prism");

    [Fact]
    public void Build_with_match_result_embeds_annotations_css()
    {
        var matched = new Spectacle.Annotations.MatchResult(
            System.Array.Empty<Spectacle.Annotations.MatchedComment>(),
            System.Array.Empty<Spectacle.Annotations.Comment>());
        var html = PreviewHtml.Build("<p>hi</p>", "https://h/", PreviewTheme.Dark, matched);

        html.Should().Contain(".md-block");
        html.Should().Contain(".sp-composer");
    }

    [Fact]
    public void Build_with_match_result_embeds_annotations_js()
    {
        var matched = new Spectacle.Annotations.MatchResult(
            System.Array.Empty<Spectacle.Annotations.MatchedComment>(),
            System.Array.Empty<Spectacle.Annotations.Comment>());
        var html = PreviewHtml.Build("", "x", PreviewTheme.Dark, matched);

        html.Should().Contain("__spectacleAnnotations__");
        html.Should().Contain("postMessage");
    }

    [Fact]
    public void Build_with_match_result_includes_matched_comments_in_payload()
    {
        var anchor = new Spectacle.Annotations.BlockAnchor("paragraph", 1, "h", 0, "lead");
        var c = new Spectacle.Annotations.Comment("c1", anchor, "orig", "rev",
            new System.DateTime(2026, 5, 15, 0, 0, 0, System.DateTimeKind.Utc), null);
        var b = new Spectacle.Render.TaggedBlock("b0", "paragraph", 1, "h", 0, "orig");
        var match = new Spectacle.Annotations.MatchedComment(c, b);
        var matched = new Spectacle.Annotations.MatchResult(
            new[] { match },
            System.Array.Empty<Spectacle.Annotations.Comment>());

        var html = PreviewHtml.Build("", "x", PreviewTheme.Dark, matched);

        html.Should().Contain("\"c1\"");
        html.Should().Contain("\"blockIdAtRender\":\"b0\"");
        html.Should().Contain("\"rev\"");
    }

    [Fact]
    public void Payload_escapes_closing_script_tag_in_comment_body()
    {
        var anchor = new Spectacle.Annotations.BlockAnchor("paragraph", 1, "h", 0, "lead");
        var c = new Spectacle.Annotations.Comment("c1", anchor, "orig",
            "</script><script>alert('xss')</script>",
            new System.DateTime(2026, 5, 15, 0, 0, 0, System.DateTimeKind.Utc), null);
        var b = new Spectacle.Render.TaggedBlock("b0", "paragraph", 1, "h", 0, "orig");
        var match = new Spectacle.Annotations.MatchedComment(c, b);
        var matched = new Spectacle.Annotations.MatchResult(
            new[] { match },
            System.Array.Empty<Spectacle.Annotations.Comment>());

        var html = PreviewHtml.Build("", "x", PreviewTheme.Dark, matched);

        // Defense in depth: JsonSerializer's default JavaScriptEncoder escapes '<' and
        // '>' to \uXXXX so a `</script>` in user content cannot terminate the surrounding
        // <script> tag. The additional `</` -> `<\/` replace covers the case where the
        // encoder is ever relaxed. Either form is acceptable; the payload must not
        // contain a raw `</script>`.
        html.Should().NotContain("</script><script>alert");
        // Confirm the user-supplied `<` is encoded (System.Text.Json emits <).
        html.ToLowerInvariant().Should().Contain("\\u003c/script\\u003e");
    }

    [Fact]
    public void Build_embeds_keynav_css_after_annotations_css()
    {
        var matched = new Spectacle.Annotations.MatchResult(
            System.Array.Empty<Spectacle.Annotations.MatchedComment>(),
            System.Array.Empty<Spectacle.Annotations.Comment>());
        var html = PreviewHtml.Build("", "x", PreviewTheme.Dark, matched);

        var annotationsCssMarker = html.IndexOf(".sp-composer");
        var keynavCssMarker = html.IndexOf("preview-keynav.css — focus indicators");

        annotationsCssMarker.Should().BeGreaterThan(0, "annotations CSS must still be embedded");
        keynavCssMarker.Should().BeGreaterThan(0, "keynav CSS must be embedded");
        keynavCssMarker.Should().BeGreaterThan(annotationsCssMarker,
            "keynav CSS must appear after annotations CSS so its rules win on conflict");
    }

    [Fact]
    public void Build_embeds_keynav_js_after_annotations_js()
    {
        var matched = new Spectacle.Annotations.MatchResult(
            System.Array.Empty<Spectacle.Annotations.MatchedComment>(),
            System.Array.Empty<Spectacle.Annotations.Comment>());
        var html = PreviewHtml.Build("", "x", PreviewTheme.Dark, matched);

        var annotationsJsMarker = html.IndexOf("__spectacleAnnotations__");
        var keynavJsMarker = html.IndexOf("preview-keynav.js — keyboard focus controller");

        annotationsJsMarker.Should().BeGreaterThan(0, "annotations JS payload must be present");
        keynavJsMarker.Should().BeGreaterThan(0, "keynav JS must be embedded");
        keynavJsMarker.Should().BeGreaterThan(annotationsJsMarker,
            "keynav JS must load after annotations JS so it can call into __sp_* helpers");
    }

    [Fact]
    public void Build_without_match_result_still_includes_keynav()
    {
        // Even without comments, keynav must be present so block navigation
        // works on plain documents.
        var html = PreviewHtml.Build("<p>hi</p>", "x", PreviewTheme.Dark);

        html.Should().Contain("preview-keynav.js — keyboard focus controller");
        html.Should().Contain("preview-keynav.css — focus indicators");
    }

    [Fact]
    public void Keynav_js_declares_single_keymap_constant()
    {
        var html = PreviewHtml.Build("<p>hi</p>", "x", PreviewTheme.Dark);

        // KEYMAP is the single source of truth; it must be declared exactly once
        // (the dispatcher and the overlay both read from it).
        var occurrences = System.Text.RegularExpressions.Regex
            .Matches(html, @"\bvar KEYMAP\s*=\s*\{").Count;

        occurrences.Should().Be(1, "KEYMAP must be declared exactly once in preview-keynav.js");
        html.Should().Contain("preview-wide");
        html.Should().Contain("on-block");
        html.Should().Contain("on-card");
        html.Should().Contain("on-orphan");
        html.Should().Contain("in-composer");
        html.Should().Contain("in-reanchor");
    }

    [Fact]
    public void Keynav_css_gives_focusables_scroll_margin()
    {
        var html = PreviewHtml.Build("<p>hi</p>", "x", PreviewTheme.Dark);

        // One rule must cover all three focusable kinds so every keyboard-focus
        // scrollIntoView lands the target with breathing room, not flush at the edge.
        System.Text.RegularExpressions.Regex.IsMatch(html,
                @"\.md-block,\s*\.sp-card,\s*\.sp-orphan-row\s*\{[^}]*scroll-margin-top:\s*48px;[^}]*scroll-margin-bottom:\s*48px;")
            .Should().BeTrue("keynav CSS must give .md-block, .sp-card and .sp-orphan-row 48px scroll margins");
    }

    [Fact]
    public void Keynav_js_scrolls_focus_target_unconditionally()
    {
        var html = PreviewHtml.Build("<p>hi</p>", "x", PreviewTheme.Dark);

        // The zero-pixels-visible gate left sliver-visible blocks below the fold
        // (spec 2026-06-11). Focus must always scrollIntoView; the helper is gone.
        html.Should().NotContain("isFullyOffscreen");
    }

    [Fact]
    public void Build_embeds_find_assets_on_plain_documents()
    {
        // Find must be available on any document, with or without comments.
        var html = PreviewHtml.Build("<p>hi</p>", "x", PreviewTheme.Dark);

        html.Should().Contain("preview-find.js — in-document text search");
        html.Should().Contain("preview-find.css — Find-in-document bar");
        html.Should().Contain("::highlight(sp-find)");
    }

    [Fact]
    public void Build_embeds_outline_assets_on_plain_documents()
    {
        var html = PreviewHtml.Build("<p>hi</p>", "x", PreviewTheme.Dark);

        html.Should().Contain("preview-outline.js — document outline");
        html.Should().Contain("preview-outline.css — document outline");
    }

    [Fact]
    public void Build_emits_outline_payload_even_when_null()
    {
        var html = PreviewHtml.Build("<p>hi</p>", "x", PreviewTheme.Dark);

        // The global must always exist so preview-outline.js can read it safely.
        html.Should().Contain("window.__spectacleOutline__ = [];");
    }

    [Fact]
    public void Build_includes_outline_entries_in_payload()
    {
        var outline = new[]
        {
            new OutlineEntry(1, "Intro", "intro", 1),
            new OutlineEntry(2, "Details", "details", 5)
        };
        var html = PreviewHtml.Build("", "x", PreviewTheme.Dark, matchResult: null, outline);

        html.Should().Contain("\"text\":\"Intro\"");
        html.Should().Contain("\"id\":\"intro\"");
        html.Should().Contain("\"level\":2");
        html.Should().Contain("\"line\":5");
    }

    [Fact]
    public void Outline_payload_escapes_closing_script_tag_in_heading_text()
    {
        var outline = new[]
        {
            new OutlineEntry(1, "</script><script>alert('xss')</script>", "h", 1)
        };
        var html = PreviewHtml.Build("", "x", PreviewTheme.Dark, matchResult: null, outline);

        html.Should().NotContain("</script><script>alert");
    }

    [Fact]
    public void Build_keymap_documents_find_and_outline_sections()
    {
        var html = PreviewHtml.Build("<p>hi</p>", "x", PreviewTheme.Dark);

        html.Should().Contain("in-find");
        html.Should().Contain("in-outline");
        html.Should().Contain("Find in document");
        html.Should().Contain("Toggle document outline");
    }
}
