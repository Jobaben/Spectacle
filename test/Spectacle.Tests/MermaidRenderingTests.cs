using System.Text.Json;
using FluentAssertions;
using Spectacle.Checks;
using Spectacle.Export;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class MermaidRenderingTests
{
    private const string Diagram = "```mermaid\nflowchart TD\n  A-->B\n```\n";
    private const string NoDiagram = "# Title\n\nJust prose, and a `code span`.\n";

    private static string Body(string markdown) => new MdRenderer().ToHtml(markdown);

    // ---------- the emitted container ----------

    [Fact]
    public void A_mermaid_fence_becomes_a_marked_figure()
    {
        var html = Body(Diagram);

        html.Should().Contain("<figure");
        html.Should().Contain($"{MermaidDiagram.Marker}=\"{MermaidDiagram.PendingState}\"");
    }

    [Fact]
    public void The_figure_carries_the_blocks_identity()
    {
        // A diagram has to be a first-class block: focusable by keyboard navigation, anchorable by a
        // comment, addressable from the outline. All of that rides on BlockTagger's attributes, which
        // land on the element the renderer opens.
        var html = Body(Diagram);

        html.Should().Contain("class=\"language-mermaid md-block\"");
        html.Should().Contain("data-block-id=\"b0\"");
        html.Should().Contain("data-kind=\"code\"");
        html.Should().Contain("data-line=\"1\"");
        html.Should().Contain("tabindex=\"0\"");
        html.Should().Contain("data-text-hash=");
    }

    [Fact]
    public void The_diagram_source_is_kept_inside_the_figure()
    {
        // This is the no-script rendering: without the bundle the document still shows the diagram's
        // definition as a code block, which is what it rendered as before diagrams were drawn.
        Body(Diagram).Should().Contain($"<pre class=\"{MermaidDiagram.SourceClass}\"><code>");
    }

    [Fact]
    public void The_diagram_source_is_html_escaped()
    {
        // Arrows and labels are full of < and >; unescaped they would close the element.
        var html = Body("```mermaid\nflowchart TD\n  A-->B & C\n  D[\"<b>label</b>\"]\n```\n");

        html.Should().Contain("A--&gt;B");
        html.Should().NotContain("<b>label</b>");
        html.Should().Contain("&lt;b&gt;label&lt;/b&gt;");
    }

    [Fact]
    public void A_fence_in_another_language_still_renders_as_a_code_block()
    {
        var html = Body("```json\n{\"a\": 1}\n```\n");

        html.Should().Contain("<pre><code class=\"language-json");
        html.Should().NotContain(MermaidDiagram.Marker);
        html.Should().NotContain("<figure");
    }

    [Fact]
    public void An_untagged_fence_still_renders_as_a_code_block()
    {
        Body("```\nplain\n```\n").Should().NotContain(MermaidDiagram.Marker);
    }

    [Fact]
    public void An_indented_code_block_still_renders_as_a_code_block()
    {
        // Not every CodeBlock is a FencedCodeBlock, and the diagram renderer takes over the whole
        // code-block slot.
        var html = Body("text\n\n    indented code\n");

        html.Should().Contain("<pre><code");
        html.Should().Contain("indented code");
        html.Should().NotContain(MermaidDiagram.Marker);
    }

    [Fact]
    public void The_metadata_header_is_still_not_rendered()
    {
        // Markdig models a YAML front-matter header as a CodeBlock, and UseYamlFrontMatter suppresses
        // it with a renderer of its own. Installing the diagram renderer ahead of that one claims the
        // header first and prints the document's metadata as a visible code block — which is what
        // happened, and is why the renderer is installed at the replaced renderer's own index.
        // FrontMatterRenderingTests owns this behaviour; it is asserted here as well because this
        // renderer is what can break it.
        var html = Body("---\ntitle: Auth design\nstatus: draft\n---\n\n# Auth\n\nText.\n");

        html.Should().NotContain("title: Auth design");
        html.Should().NotContain("<pre>");
        html.Should().Contain("<h1");
    }

    [Fact]
    public void A_metadata_header_and_a_diagram_coexist()
    {
        var html = Body("---\ntitle: Auth design\n---\n\n# Auth\n\n" + Diagram);

        html.Should().NotContain("title: Auth design");
        MermaidDiagram.IsRenderedIn(html).Should().BeTrue();
    }

    // ---------- which fences count ----------

    [Theory]
    [InlineData("mermaid", true)]
    [InlineData("Mermaid", true)]
    [InlineData("MERMAID", true)]
    [InlineData("mermaid extra=1", true)]
    [InlineData("mermaidx", false)]
    [InlineData("json", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_a_mermaid_language_token_opens_a_diagram(string? info, bool expected) =>
        MermaidDiagram.IsDiagramFence(info).Should().Be(expected);

    // ---------- conditional assets ----------

    [Fact]
    public void A_document_with_a_diagram_carries_the_renderer()
    {
        var html = PreviewHtml.Build(Body(Diagram), "x", PreviewTheme.Dark);

        html.Should().Contain("__spectacleMermaid__");        // the theme configuration
        html.Should().Contain("globalThis[\"mermaid\"]");     // the vendored bundle
        html.Should().Contain(".mermaid-diagram");            // the stylesheet
    }

    [Fact]
    public void A_document_with_no_diagram_carries_none_of_it()
    {
        // The bundle is 3.4 MB. Inlining it unconditionally would make a one-paragraph export three
        // thousand times the size of its own prose and pay mermaid's start-up cost on every document.
        var html = PreviewHtml.Build(Body(NoDiagram), "x", PreviewTheme.Dark);

        html.Should().NotContain("mermaid");
    }

    [Fact]
    public void An_export_with_a_diagram_draws_it_offline_too()
    {
        var html = HtmlExporter.FromMarkdown(Diagram, PreviewTheme.Dark, "Doc");

        html.Should().Contain("globalThis[\"mermaid\"]");
        html.Should().Contain("__spectacleMermaid__");
        html.Should().Contain(".mermaid-diagram");
    }

    [Fact]
    public void An_export_with_no_diagram_carries_none_of_it()
    {
        HtmlExporter.FromMarkdown(NoDiagram, PreviewTheme.Dark, "Doc")
            .Should().NotContain("mermaid");
    }

    [Fact]
    public void The_export_stays_self_contained()
    {
        // The whole point of the export is a file that renders with no network. A diagram must not
        // introduce the first outbound request.
        var html = HtmlExporter.FromMarkdown(Diagram, PreviewTheme.Dark, "Doc");

        html.Should().NotContain("<script src=");
        html.Should().NotContain("https://cdn.");
        html.Should().NotContain("unpkg.com");
    }

    [Fact]
    public void Detection_keys_on_the_marker_the_renderer_emits()
    {
        MermaidDiagram.IsRenderedIn(Body(Diagram)).Should().BeTrue();
        MermaidDiagram.IsRenderedIn(Body(NoDiagram)).Should().BeFalse();
        MermaidDiagram.IsRenderedIn("").Should().BeFalse();
        MermaidDiagram.IsRenderedIn(null).Should().BeFalse();
    }

    [Fact]
    public void Asset_helpers_are_empty_for_a_document_with_no_diagram()
    {
        MermaidAssets.HeadFor(Body(NoDiagram)).Should().BeEmpty();
        MermaidAssets.BodyFor(Body(NoDiagram), PreviewTheme.Dark).Should().BeEmpty();
    }

    // ---------- the configuration handed to mermaid ----------

    private static JsonElement Config(PreviewTheme theme) =>
        JsonDocument.Parse(MermaidAssets.ConfigJson(theme)).RootElement;

    [Fact]
    public void The_script_renders_each_diagram_itself()
    {
        // startOnLoad off is what lets preview-mermaid.js catch a syntax error per diagram and leave
        // that one showing its source while the rest of the page draws.
        Config(PreviewTheme.Dark).GetProperty("startOnLoad").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void Diagram_text_is_treated_as_untrusted()
    {
        // Spectacle's subject is frequently a document a model wrote, so a diagram must not be able
        // to run script or reach outside itself.
        Config(PreviewTheme.Dark).GetProperty("securityLevel").GetString().Should().Be("strict");
    }

    [Fact]
    public void Ids_are_deterministic()
    {
        // The preview re-renders the whole document on every change an agent makes to the file.
        Config(PreviewTheme.Dark).GetProperty("deterministicIds").GetBoolean().Should().BeTrue();
        MermaidAssets.ConfigJson(PreviewTheme.Dark)
            .Should().Be(MermaidAssets.ConfigJson(PreviewTheme.Dark));
    }

    [Fact]
    public void Each_theme_hands_mermaid_its_own_palette()
    {
        var dark = Config(PreviewTheme.Dark).GetProperty("themeVariables");
        var light = Config(PreviewTheme.Light).GetProperty("themeVariables");
        var hc = Config(PreviewTheme.HighContrast).GetProperty("themeVariables");

        dark.GetProperty("background").GetString().Should().Be(MermaidPalette.Dark.Background);
        light.GetProperty("background").GetString().Should().Be(MermaidPalette.Light.Background);
        hc.GetProperty("background").GetString().Should().Be(MermaidPalette.HighContrast.Background);
        dark.GetProperty("primaryTextColor").GetString().Should().Be(MermaidPalette.Dark.NodeText);
        light.GetProperty("primaryTextColor").GetString().Should().Be(MermaidPalette.Light.NodeText);
        hc.GetProperty("primaryTextColor").GetString().Should().Be("#ffffff");
    }

    [Fact]
    public void Only_the_light_theme_turns_dark_mode_off()
    {
        // darkMode steers what mermaid derives for anything the palette does not name outright. On a
        // near-white canvas the dark-mode derivations come out too pale to see.
        Config(PreviewTheme.Dark).GetProperty("darkMode").GetBoolean().Should().BeTrue();
        Config(PreviewTheme.HighContrast).GetProperty("darkMode").GetBoolean().Should().BeTrue();
        Config(PreviewTheme.Light).GetProperty("darkMode").GetBoolean().Should().BeFalse();

        Config(PreviewTheme.Light).GetProperty("themeVariables")
            .GetProperty("darkMode").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void The_canvas_colour_is_the_panel_the_diagram_is_drawn_on()
    {
        // mermaid.css draws the diagram on a code-block panel, and mermaid paints this colour behind
        // edge labels. Against the page colour instead, every edge label sits in a mismatched patch.
        MermaidPalette.Dark.Background.Should().Be("#252526");
        Config(PreviewTheme.Dark).GetProperty("themeVariables")
            .GetProperty("edgeLabelBackground").GetString().Should().Be("#252526");
    }

    [Fact]
    public void Categorical_slots_are_filled_from_the_palette_in_order()
    {
        var vars = Config(PreviewTheme.Dark).GetProperty("themeVariables");
        var series = MermaidPalette.Dark.Series;

        for (var i = 0; i < series.Count; i++)
        {
            vars.GetProperty($"pie{i + 1}").GetString().Should().Be(series[i]);
            vars.GetProperty($"git{i}").GetString().Should().Be(series[i]);
            vars.GetProperty($"cScale{i}").GetString().Should().Be(series[i]);
        }
    }

    [Fact]
    public void A_series_past_the_palette_holds_at_the_neutral_rather_than_cycling()
    {
        // Wrapping back to the first hue would give two categories one colour, which is a chart that
        // states something false. The neutral says the palette has run out.
        var vars = Config(PreviewTheme.Dark).GetProperty("themeVariables");
        var overflow = MermaidPalette.Dark.SeriesOverflow;

        for (var slot = MermaidPalette.Dark.Series.Count + 1; slot <= 12; slot++)
            vars.GetProperty($"pie{slot}").GetString().Should().Be(overflow);

        overflow.Should().NotBe(MermaidPalette.Dark.Series[0]);
    }

    [Fact]
    public void Pie_slices_are_drawn_at_full_opacity()
    {
        // mermaid otherwise blends each slice toward the canvas at 0.7, which moves every fill off
        // the colour its label's contrast was measured against.
        Config(PreviewTheme.Dark).GetProperty("themeVariables")
            .GetProperty("pieOpacity").GetInt32().Should().Be(1);
    }

    [Fact]
    public void Xychart_axes_are_given_the_documents_own_text_colour()
    {
        // xychart does not read the shared text variables; left unset its labels derive to a
        // near-black olive that cannot be read on the panel.
        var xy = Config(PreviewTheme.Dark).GetProperty("themeVariables").GetProperty("xyChart");

        xy.GetProperty("xAxisLabelColor").GetString().Should().Be(MermaidPalette.Dark.Text);
        xy.GetProperty("yAxisLabelColor").GetString().Should().Be(MermaidPalette.Dark.Text);
        xy.GetProperty("plotColorPalette").GetString().Should().Contain(MermaidPalette.Dark.Series[0]);
    }

    [Fact]
    public void The_palette_follows_the_theme()
    {
        MermaidPalette.For(PreviewTheme.Dark).Should().BeSameAs(MermaidPalette.Dark);
        MermaidPalette.For(PreviewTheme.Light).Should().BeSameAs(MermaidPalette.Light);
        MermaidPalette.For(PreviewTheme.HighContrast).Should().BeSameAs(MermaidPalette.HighContrast);
    }

    [Fact]
    public void A_light_export_carries_the_light_diagram_palette()
    {
        // The end-to-end version of the same thing: --export-html --light on a document with a
        // diagram must not ship the dark canvas colour to a light page.
        var html = HtmlExporter.FromMarkdown(Diagram, PreviewTheme.Light, "Doc");

        html.Should().Contain($"\"background\":\"{MermaidPalette.Light.Background}\"");
        html.Should().NotContain($"\"background\":\"{MermaidPalette.Dark.Background}\"");
    }
}
