using System.Linq;
using Xunit;
using FluentAssertions;
using Spectacle.Accessibility;
using Spectacle.Render;

namespace Spectacle.Tests;

public class PaletteContrastTests
{
    // Dark+ palette from src/Spectacle/Render/Assets/dark.css
    private const string DarkBg = "#1e1e1e";
    private const string DarkFg = "#d4d4d4";
    private const string DarkLink = "#4ea1ff";
    private const string DarkFocus = "#7cb7ff";
    private const string DarkMuted = "#9da5b4";
    private const string DarkCodeBg = "#252526";

    // Light palette from src/Spectacle/Render/Assets/light.css
    private const string LightBg = "#ffffff";
    private const string LightFg = "#1f2328";
    private const string LightLink = "#0969da";
    private const string LightFocus = "#0550ae";
    private const string LightMuted = "#656d76";
    private const string LightCodeBg = "#f6f8fa";
    private const string LightGatePass = "#1a7f37";
    private const string LightGateError = "#cf222e";
    private const string LightGateWarning = "#9a6700";
    private const string LightGateInfo = "#0550ae";

    // Gate severities from src/Spectacle/Render/Assets/dark.css
    private const string DarkGatePass = "#89d185";
    private const string DarkGateError = "#f48771";
    private const string DarkGateWarning = "#dcdcaa";
    private const string DarkGateInfo = "#9cdcfe";

    // High-contrast palette from src/Spectacle/Render/Assets/hc.css
    private const string HcBg = "#000000";
    private const string HcFg = "#ffffff";
    private const string HcLink = "#ffff00";

    [Fact]
    public void Dark_body_meets_AAA() =>
        WcagContrast.Ratio(DarkFg, DarkBg).Should().BeGreaterThanOrEqualTo(7.0);

    [Fact]
    public void Dark_link_meets_AA() =>
        WcagContrast.Ratio(DarkLink, DarkBg).Should().BeGreaterThanOrEqualTo(4.5);

    [Fact]
    public void Dark_focus_outline_meets_AA() =>
        WcagContrast.Ratio(DarkFocus, DarkBg).Should().BeGreaterThanOrEqualTo(3.0);

    [Fact]
    public void Dark_muted_meets_AA() =>
        WcagContrast.Ratio(DarkMuted, DarkBg).Should().BeGreaterThanOrEqualTo(4.5);

    [Fact]
    public void Dark_body_on_code_bg_meets_AAA() =>
        WcagContrast.Ratio(DarkFg, DarkCodeBg).Should().BeGreaterThanOrEqualTo(7.0);

    [Fact]
    public void Light_body_meets_AAA() =>
        WcagContrast.Ratio(LightFg, LightBg).Should().BeGreaterThanOrEqualTo(7.0);

    [Fact]
    public void Light_link_meets_AA() =>
        WcagContrast.Ratio(LightLink, LightBg).Should().BeGreaterThanOrEqualTo(4.5);

    [Fact]
    public void Light_focus_outline_meets_AA() =>
        WcagContrast.Ratio(LightFocus, LightBg).Should().BeGreaterThanOrEqualTo(3.0);

    [Fact]
    public void Light_muted_meets_AA() =>
        WcagContrast.Ratio(LightMuted, LightBg).Should().BeGreaterThanOrEqualTo(4.5);

    [Fact]
    public void Light_body_on_code_bg_meets_AAA() =>
        WcagContrast.Ratio(LightFg, LightCodeBg).Should().BeGreaterThanOrEqualTo(7.0);

    [Fact]
    public void Dark_gate_severities_meet_AA()
    {
        foreach (var sev in new[] { DarkGatePass, DarkGateError, DarkGateWarning, DarkGateInfo })
            WcagContrast.Ratio(sev, DarkBg).Should().BeGreaterThanOrEqualTo(4.5, sev);
    }

    [Fact]
    public void Light_gate_severities_meet_AA()
    {
        // A severity label is text on the page colour (the findings panel sits on --code-bg, which
        // is darker still, so --bg is the worst case). The dark theme's hues are the reason this is
        // asserted per theme rather than once: #f48771 and the rest land near 2.5:1 on white.
        foreach (var sev in new[] { LightGatePass, LightGateError, LightGateWarning, LightGateInfo })
            WcagContrast.Ratio(sev, LightBg).Should().BeGreaterThanOrEqualTo(4.5, sev);
    }

    [Fact]
    public void The_light_theme_does_not_inherit_the_dark_severity_hues()
    {
        // preview-gate.css falls back to the dark values when a theme leaves --gate-* unset, and the
        // fallback is silent — so the guard is that light.css names its own.
        var light = PreviewHtml.ThemeCss(PreviewTheme.Light);

        foreach (var name in new[] { "--gate-pass", "--gate-error", "--gate-warning", "--gate-info" })
            light.Should().Contain(name);
    }

    [Fact]
    public void Hc_body_meets_AAA() =>
        WcagContrast.Ratio(HcFg, HcBg).Should().BeGreaterThanOrEqualTo(7.0);

    [Fact]
    public void Hc_link_meets_AA() =>
        WcagContrast.Ratio(HcLink, HcBg).Should().BeGreaterThanOrEqualTo(4.5);

    // ---------- Diagrams ----------
    //
    // Mermaid paints with inline SVG attributes, so a diagram cannot inherit the stylesheet's colours
    // and is handed its own palette instead (MermaidPalette). A drawn diagram carries as much of a
    // document's meaning as its prose, so the same ratios are asserted on it: AAA for text, and the
    // 3:1 WCAG 1.4.11 floor for the graphics that carry information — borders, edges, series fills.

    private static readonly MermaidPalette Diagram = MermaidPalette.Dark;

    [Fact]
    public void Dark_diagram_label_meets_AAA_on_its_node() =>
        WcagContrast.Ratio(Diagram.NodeText, Diagram.NodeFill).Should().BeGreaterThanOrEqualTo(7.0);

    [Fact]
    public void Dark_diagram_text_on_the_canvas_meets_AAA() =>
        WcagContrast.Ratio(Diagram.Text, Diagram.Background).Should().BeGreaterThanOrEqualTo(7.0);

    [Fact]
    public void Dark_diagram_text_in_a_subgraph_meets_AAA() =>
        WcagContrast.Ratio(Diagram.Text, Diagram.ClusterFill).Should().BeGreaterThanOrEqualTo(7.0);

    [Fact]
    public void Dark_diagram_note_text_meets_AAA() =>
        WcagContrast.Ratio(Diagram.NoteText, Diagram.NoteFill).Should().BeGreaterThanOrEqualTo(7.0);

    [Fact]
    public void Dark_diagram_node_border_is_a_visible_graphic()
    {
        // A node's boundary is the shape that says "this is one box", against both the canvas it sits
        // on and the fill it encloses.
        WcagContrast.Ratio(Diagram.NodeBorder, Diagram.Background).Should().BeGreaterThanOrEqualTo(3.0);
        WcagContrast.Ratio(Diagram.NodeBorder, Diagram.NodeFill).Should().BeGreaterThanOrEqualTo(3.0);
    }

    [Fact]
    public void Dark_diagram_edges_and_subgraph_borders_are_visible_graphics()
    {
        WcagContrast.Ratio(Diagram.Line, Diagram.Background).Should().BeGreaterThanOrEqualTo(3.0);
        WcagContrast.Ratio(Diagram.ClusterBorder, Diagram.Background).Should().BeGreaterThanOrEqualTo(3.0);
    }

    [Fact]
    public void Dark_diagram_series_fills_are_visible_on_the_canvas()
    {
        foreach (var fill in Diagram.Series)
            WcagContrast.Ratio(fill, Diagram.Background).Should().BeGreaterThanOrEqualTo(3.0, fill);
    }

    [Fact]
    public void Dark_diagram_series_labels_are_readable_on_every_fill()
    {
        // A pie slice's percentage is drawn on the slice, and mermaid allows one ink colour for all of
        // them — so the choice is whichever keeps the *worst* fill readable.
        foreach (var fill in Diagram.Series)
            WcagContrast.Ratio(Diagram.SeriesInk, fill).Should().BeGreaterThanOrEqualTo(4.0, fill);
    }

    [Fact]
    public void Dark_diagram_series_ink_is_the_better_of_black_and_white()
    {
        // This is the reason SeriesInk is black rather than white, kept as an assertion so a future
        // palette change has to re-make the choice rather than inherit it: over these eight hues
        // black's worst case is 4.25:1 where white's is 3.07:1.
        var black = Diagram.Series.Min(f => WcagContrast.Ratio("#000000", f));
        var white = Diagram.Series.Min(f => WcagContrast.Ratio("#ffffff", f));

        black.Should().BeGreaterThan(white);
        Diagram.SeriesInk.Should().Be("#000000");
    }

    [Fact]
    public void Dark_diagram_series_hues_are_distinct_and_never_cycled()
    {
        Diagram.Series.Should().OnlyHaveUniqueItems();
        Diagram.Series.Should().HaveCount(8);
    }

    [Fact]
    public void Dark_diagram_overflow_neutral_is_visible_and_labelled()
    {
        WcagContrast.Ratio(Diagram.SeriesOverflow, Diagram.Background).Should().BeGreaterThanOrEqualTo(3.0);
        WcagContrast.Ratio(Diagram.SeriesInk, Diagram.SeriesOverflow).Should().BeGreaterThanOrEqualTo(4.5);
    }

    private static readonly MermaidPalette LightDiagram = MermaidPalette.Light;

    [Fact]
    public void Light_diagram_text_meets_AAA_everywhere_it_is_drawn()
    {
        WcagContrast.Ratio(LightDiagram.NodeText, LightDiagram.NodeFill).Should().BeGreaterThanOrEqualTo(7.0);
        WcagContrast.Ratio(LightDiagram.Text, LightDiagram.Background).Should().BeGreaterThanOrEqualTo(7.0);
        WcagContrast.Ratio(LightDiagram.Text, LightDiagram.ClusterFill).Should().BeGreaterThanOrEqualTo(7.0);
        WcagContrast.Ratio(LightDiagram.NoteText, LightDiagram.NoteFill).Should().BeGreaterThanOrEqualTo(7.0);
    }

    [Fact]
    public void Light_diagram_borders_and_edges_are_visible_graphics()
    {
        WcagContrast.Ratio(LightDiagram.NodeBorder, LightDiagram.Background).Should().BeGreaterThanOrEqualTo(3.0);
        WcagContrast.Ratio(LightDiagram.NodeBorder, LightDiagram.NodeFill).Should().BeGreaterThanOrEqualTo(3.0);
        WcagContrast.Ratio(LightDiagram.Line, LightDiagram.Background).Should().BeGreaterThanOrEqualTo(3.0);
        WcagContrast.Ratio(LightDiagram.ClusterBorder, LightDiagram.Background).Should().BeGreaterThanOrEqualTo(3.0);
    }

    [Fact]
    public void Light_diagram_series_fills_are_visible_and_labelled()
    {
        foreach (var fill in LightDiagram.Series)
        {
            WcagContrast.Ratio(fill, LightDiagram.Background).Should().BeGreaterThanOrEqualTo(3.0, fill);
            WcagContrast.Ratio(LightDiagram.SeriesInk, fill).Should().BeGreaterThanOrEqualTo(4.0, fill);
        }
    }

    [Fact]
    public void Light_diagram_series_ink_is_the_better_of_black_and_white()
    {
        // The mirror of the dark palette's choice, and it inverts: a fill dark enough to clear 3:1 on
        // a near-white canvas is also dark enough that black ink on it is unreadable.
        var black = LightDiagram.Series.Min(f => WcagContrast.Ratio("#000000", f));
        var white = LightDiagram.Series.Min(f => WcagContrast.Ratio("#ffffff", f));

        white.Should().BeGreaterThan(black);
        LightDiagram.SeriesInk.Should().Be("#ffffff");
    }

    [Fact]
    public void Light_diagram_series_hues_are_distinct_and_never_cycled()
    {
        LightDiagram.Series.Should().OnlyHaveUniqueItems();
        LightDiagram.Series.Should().HaveCount(8);
    }

    [Fact]
    public void Light_diagram_overflow_neutral_is_visible_and_labelled()
    {
        WcagContrast.Ratio(LightDiagram.SeriesOverflow, LightDiagram.Background)
            .Should().BeGreaterThanOrEqualTo(3.0);
        WcagContrast.Ratio(LightDiagram.SeriesInk, LightDiagram.SeriesOverflow)
            .Should().BeGreaterThanOrEqualTo(4.5);
    }

    [Fact]
    public void Light_diagram_canvas_is_the_light_code_panel()
    {
        // The bug this pins: with no light palette, MermaidPalette.For fell through to Dark and drew
        // a #252526 canvas with #d4d4d4 labels inside a #f6f8fa code panel.
        LightDiagram.Background.Should().Be(LightCodeBg);
        LightDiagram.Text.Should().Be(LightFg);
    }

    [Fact]
    public void Hc_diagram_is_pure_black_and_white()
    {
        var hc = MermaidPalette.HighContrast;
        var everything = new[]
        {
            hc.Background, hc.NodeFill, hc.NodeText, hc.NodeBorder, hc.Line, hc.Text,
            hc.ClusterFill, hc.ClusterBorder, hc.NoteFill, hc.NoteText,
            hc.SeriesOverflow, hc.SeriesInk, hc.SeriesStroke,
        }.Concat(hc.Series);

        everything.Should().AllSatisfy(c => c.Should().BeOneOf("#000000", "#ffffff"));
    }

    [Fact]
    public void Hc_diagram_text_and_graphics_are_at_the_maximum()
    {
        var hc = MermaidPalette.HighContrast;

        WcagContrast.Ratio(hc.NodeText, hc.NodeFill).Should().BeGreaterThanOrEqualTo(7.0);
        WcagContrast.Ratio(hc.NodeBorder, hc.Background).Should().BeGreaterThanOrEqualTo(7.0);
        WcagContrast.Ratio(hc.Line, hc.Background).Should().BeGreaterThanOrEqualTo(7.0);
    }

    [Fact]
    public void Hc_diagram_series_are_separated_by_their_outline_not_their_fill()
    {
        // High contrast encodes no identity by fill: a grey ramp is not a categorical palette (no
        // chroma, and the steps dark enough to hold white labels fall under 3:1 on black). Every
        // series is drawn as the canvas with a white outline, so slices are individually visible —
        // which is what was broken, a pie chart of black on black. The outline is load-bearing here,
        // so it is the thing asserted.
        var hc = MermaidPalette.HighContrast;

        hc.Series.Should().AllSatisfy(f => f.Should().Be(hc.Background));
        WcagContrast.Ratio(hc.SeriesStroke, hc.Background).Should().BeGreaterThanOrEqualTo(7.0);
        WcagContrast.Ratio(hc.SeriesInk, hc.Background).Should().BeGreaterThanOrEqualTo(7.0);
    }
}
