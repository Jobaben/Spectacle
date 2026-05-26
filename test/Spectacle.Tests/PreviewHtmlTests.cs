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
}
