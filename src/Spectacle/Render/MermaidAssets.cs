using System.Text.Json;

namespace Spectacle.Render;

/// <summary>
/// The colours a diagram is drawn in, one set per <see cref="PreviewTheme"/>. Mermaid paints with
/// inline SVG attributes, so it cannot read the CSS custom properties the rest of the preview
/// styles itself from — the palette has to be handed to it as configuration. Keeping the values
/// here (rather than inline in a JSON blob) is what lets <c>PaletteContrastTests</c> assert the
/// same contrast ratios on a diagram that it asserts on the document's prose.
/// </summary>
/// <param name="Background">Canvas behind the diagram; matches the preview's own background.</param>
/// <param name="NodeFill">Fill of a node, box, or actor.</param>
/// <param name="NodeText">Label text drawn on <paramref name="NodeFill"/>.</param>
/// <param name="NodeBorder">Node outline — a meaningful graphic, so it needs 3:1 on the canvas.</param>
/// <param name="Line">Edges, arrows, and lifelines.</param>
/// <param name="Text">Text drawn straight on the canvas (edge labels, titles).</param>
/// <param name="ClusterFill">Fill of a subgraph / grouping box.</param>
/// <param name="ClusterBorder">Outline of a subgraph / grouping box.</param>
/// <param name="NoteFill">Fill of a note callout.</param>
/// <param name="NoteText">Text drawn on <paramref name="NoteFill"/>.</param>
/// <param name="Series">
/// Categorical fills for the diagrams that colour by series — pie slices, git branches, journey and
/// timeline sections, xychart plots. Assigned in this fixed order and never cycled: two series
/// wearing one colour is a chart that lies.
/// </param>
/// <param name="SeriesOverflow">
/// Fill for a series past the end of <paramref name="Series"/>. A neutral, so a chart with more
/// categories than the palette encodes shows that it has run out rather than repeating a hue.
/// </param>
/// <param name="SeriesInk">Text drawn on top of a series fill (a slice's percentage, a branch label).</param>
/// <param name="SeriesStroke">The boundary drawn between adjacent series fills.</param>
public sealed record MermaidPalette(
    string Background,
    string NodeFill,
    string NodeText,
    string NodeBorder,
    string Line,
    string Text,
    string ClusterFill,
    string ClusterBorder,
    string NoteFill,
    string NoteText,
    IReadOnlyList<string> Series,
    string SeriesOverflow,
    string SeriesInk,
    string SeriesStroke)
{
    /// <summary>
    /// Dark+ diagram palette, drawn from the same values as <c>dark.css</c>: the code-block panel
    /// the diagram is drawn on, the body foreground for label text, and the muted grey for edges.
    /// <c>Background</c> is the panel (<c>--code-bg</c>) rather than the page, because
    /// <c>mermaid.css</c> draws the diagram on a code-block panel — mermaid paints this colour
    /// behind edge labels, and against the wrong one every label sits in a faintly mismatched patch.
    /// Fills then step up from that panel so a node reads as raised on it, and the node border is
    /// Dark+'s blue, which clears 3:1 on the fill so a box has a visible boundary.
    /// </summary>
    public static readonly MermaidPalette Dark = new(
        Background: "#252526",
        NodeFill: "#2d2d30",
        NodeText: "#d4d4d4",
        NodeBorder: "#569cd6",
        Line: "#9da5b4",
        Text: "#d4d4d4",
        ClusterFill: "#37373d",
        ClusterBorder: "#9da5b4",
        NoteFill: "#37373d",
        NoteText: "#d4d4d4",
        // The eight-hue categorical palette, in its documented order, at its dark-surface steps.
        // Validated as a set against this diagram surface (#252526): every hue inside the dark
        // lightness band, above the chroma floor, at least 3:1 against the surface, worst adjacent
        // pair 8.4 ΔE under simulated protanopia and 19.3 unsimulated.
        Series: new[]
        {
            "#3987e5", // blue
            "#d95926", // orange
            "#199e70", // aqua
            "#c98500", // yellow
            "#d55181", // magenta
            "#008300", // green
            "#9085e9", // violet
            "#e66767", // red
        },
        SeriesOverflow: "#9da5b4",
        // Ink on a series fill. Black over these eight hues ranges 4.2:1 to 6.8:1, against 3.1:1 to
        // 4.9:1 for white — so black is the choice that keeps the worst slice readable. The one hue
        // that lands under 4.5:1 is the green at slot 6, which is why every diagram also keeps its
        // source in the disclosure beneath it.
        SeriesInk: "#000000",
        // The panel colour, which reads as a gap rather than a line: adjacent fills are separated by
        // the surface showing through instead of by a drawn border.
        SeriesStroke: "#252526");

    /// <summary>
    /// Light diagram palette, drawn from <c>light.css</c> the same way <see cref="Dark"/> is drawn
    /// from <c>dark.css</c>: the code-block panel (<c>--code-bg</c>) is the canvas, the body
    /// foreground is label text, and the muted grey draws edges. Node fills step *up* off the panel
    /// to white — the light-mode mirror of the dark palette's step — so a node reads as raised, and
    /// the border is the light theme's link blue, which clears 3:1 on both the panel and the fill.
    /// Without this a light document drew its diagrams in the dark palette: dark grey boxes and
    /// #d4d4d4 edge labels on a near-white panel.
    /// </summary>
    public static readonly MermaidPalette Light = new(
        Background: "#f6f8fa",
        NodeFill: "#ffffff",
        NodeText: "#1f2328",
        NodeBorder: "#0969da",
        Line: "#656d76",
        Text: "#1f2328",
        ClusterFill: "#eaeef2",
        ClusterBorder: "#656d76",
        NoteFill: "#eaeef2",
        NoteText: "#1f2328",
        // The same eight hues in the same documented order as the dark palette, at their light-
        // surface steps. On a near-white canvas a categorical fill has to be dark to clear 3:1, and
        // dark enough again to hold white ink — so these are the saturated dark ends of each hue
        // rather than the mid-tones the dark palette can afford.
        Series: new[]
        {
            "#0550ae", // blue
            "#8a4300", // orange
            "#0d6a63", // aqua
            "#6d4c00", // yellow
            "#9c1c6b", // magenta
            "#1a6b2f", // green
            "#5a32ad", // violet
            "#a4131f", // red
        },
        SeriesOverflow: "#57606a",
        // Ink on a series fill. The fills are dark by necessity here, so the choice inverts: white
        // holds 6.4:1 at worst over these eight where black manages 2.5:1.
        SeriesInk: "#ffffff",
        // The panel colour, so adjacent fills are separated by the surface showing through rather
        // than by a drawn border — the same treatment as the dark palette.
        SeriesStroke: "#f6f8fa");

    /// <summary>
    /// High-contrast diagram palette: pure black and white only, matching <c>hc.css</c>. Every
    /// distinction a colour would carry is carried by shape and by the diagram's own labels
    /// instead, which is the same trade the gate overlay makes at 21:1.
    /// </summary>
    public static readonly MermaidPalette HighContrast = new(
        Background: "#000000",
        NodeFill: "#000000",
        NodeText: "#ffffff",
        NodeBorder: "#ffffff",
        Line: "#ffffff",
        Text: "#ffffff",
        ClusterFill: "#000000",
        ClusterBorder: "#ffffff",
        NoteFill: "#000000",
        NoteText: "#ffffff",
        // High contrast encodes no identity by fill. A monochrome ramp is the only alternative on a
        // black canvas, and a grey ramp is not a categorical palette: its steps carry no chroma, and
        // the ones dark enough to hold white labels sit under 3:1 against the background. So every
        // series is drawn as the canvas with a white outline — visible and individually separated,
        // which is what was actually broken here (fills derived from a black node colour drew a pie
        // chart that was not there at all). The cost is real and specific: a legend's swatches no
        // longer say which slice is which, so the diagram's own labels and the source disclosure
        // beneath it carry that. It is the same trade hc.css makes for the gate severities, where
        // 21:1 replaces hue and the row's label does the telling apart.
        Series: new[] { "#000000" },
        SeriesOverflow: "#000000",
        SeriesInk: "#ffffff",
        SeriesStroke: "#ffffff");

    /// <summary>The palette for a preview theme.</summary>
    public static MermaidPalette For(PreviewTheme theme) => theme switch
    {
        PreviewTheme.HighContrast => HighContrast,
        PreviewTheme.Light => Light,
        _ => Dark,
    };
}

/// <summary>
/// Assembles the diagram half of a preview or exported document: the stylesheet, the vendored
/// mermaid bundle, the theme configuration, and the script that draws each diagram.
///
/// Every piece is emitted <em>only</em> when the document actually contains a diagram. The bundle
/// is 3.4 MB — inlining it unconditionally would make a one-paragraph export three thousand times
/// the size of its own prose, and would pay mermaid's start-up cost on every document Spectacle
/// opens. <see cref="MermaidDiagram.IsRenderedIn"/> is the switch.
/// </summary>
public static class MermaidAssets
{
    private static readonly Lazy<string> Css = new(() => PreviewHtml.LoadAsset("mermaid.css"));
    private static readonly Lazy<string> Bundle = new(() => PreviewHtml.LoadAsset("mermaid.min.js"));
    private static readonly Lazy<string> Script = new(() => PreviewHtml.LoadAsset("preview-mermaid.js"));

    private static readonly JsonSerializerOptions ConfigOptions = new()
    {
        WriteIndented = false,
    };

    /// <summary>
    /// The <c>&lt;style&gt;</c> element for a document containing diagrams, or an empty string for
    /// one that contains none.
    /// </summary>
    public static string HeadFor(string? bodyHtml) =>
        MermaidDiagram.IsRenderedIn(bodyHtml) ? $"<style>{Css.Value}</style>" : string.Empty;

    /// <summary>
    /// The scripts that draw the diagrams — the theme configuration, the vendored bundle, and the
    /// renderer — or an empty string for a document with no diagram.
    /// </summary>
    public static string BodyFor(string? bodyHtml, PreviewTheme theme)
    {
        if (!MermaidDiagram.IsRenderedIn(bodyHtml)) return string.Empty;

        return $"<script>window.__spectacleMermaid__ = {ConfigJson(theme)};</script>\n"
             + $"<script>{Bundle.Value}</script>\n"
             + $"<script>{Script.Value}</script>";
    }

    /// <summary>
    /// The mermaid configuration for a theme, as JSON. <c>startOnLoad</c> is off because
    /// <c>preview-mermaid.js</c> renders each diagram itself — it needs to catch a syntax error per
    /// diagram and leave that one showing its source, which mermaid's own auto-run cannot do.
    /// <c>securityLevel</c> is <c>strict</c>: a document Spectacle opens is frequently one a model
    /// wrote, so diagram text is treated as untrusted input, labels are sanitized, and the
    /// click-handler directive that would run script from a document is refused.
    /// </summary>
    public static string ConfigJson(PreviewTheme theme)
    {
        var p = MermaidPalette.For(theme);

        var config = new Dictionary<string, object>
        {
            ["startOnLoad"] = false,
            ["securityLevel"] = "strict",
            ["theme"] = "base",
            // Only the light theme turns this off: it steers the colours mermaid derives for
            // anything the palette below does not name outright, and on a near-white canvas the
            // dark-mode derivations come out too pale to see.
            ["darkMode"] = theme != PreviewTheme.Light,
            // Stable ids across renders: the preview re-renders the whole document on every
            // keystroke of an agent rewriting the file, and ids that churn defeat both diffing and
            // the browser test.
            ["deterministicIds"] = true,
            ["fontFamily"] = FontStack,
            ["fontSize"] = 16,
            // Scale the drawing to the preview's 980px column instead of overflowing it.
            ["flowchart"] = new Dictionary<string, object> { ["useMaxWidth"] = true },
            ["sequence"] = new Dictionary<string, object> { ["useMaxWidth"] = true },
            ["gantt"] = new Dictionary<string, object> { ["useMaxWidth"] = true },
            ["themeVariables"] = new Dictionary<string, object>
            {
                ["darkMode"] = theme == PreviewTheme.Dark,
                ["background"] = p.Background,
                ["fontFamily"] = FontStack,
                ["fontSize"] = "16px",

                ["primaryColor"] = p.NodeFill,
                ["primaryTextColor"] = p.NodeText,
                ["primaryBorderColor"] = p.NodeBorder,
                ["secondaryColor"] = p.ClusterFill,
                ["secondaryTextColor"] = p.NodeText,
                ["secondaryBorderColor"] = p.NodeBorder,
                ["tertiaryColor"] = p.ClusterFill,
                ["tertiaryTextColor"] = p.NodeText,
                ["tertiaryBorderColor"] = p.NodeBorder,

                ["mainBkg"] = p.NodeFill,
                ["nodeBorder"] = p.NodeBorder,
                ["nodeTextColor"] = p.NodeText,
                ["lineColor"] = p.Line,
                ["textColor"] = p.Text,
                ["titleColor"] = p.Text,
                ["clusterBkg"] = p.ClusterFill,
                ["clusterBorder"] = p.ClusterBorder,
                // Edge labels sit on top of the edge they describe, so they need the canvas colour
                // behind them or the line strikes through the text.
                ["edgeLabelBackground"] = p.Background,

                ["noteBkgColor"] = p.NoteFill,
                ["noteTextColor"] = p.NoteText,
                ["noteBorderColor"] = p.ClusterBorder,

                // Sequence diagrams name their own parts rather than reusing the node variables.
                ["actorBkg"] = p.NodeFill,
                ["actorBorder"] = p.NodeBorder,
                ["actorTextColor"] = p.NodeText,
                ["actorLineColor"] = p.Line,
                ["signalColor"] = p.Line,
                ["signalTextColor"] = p.Text,
                ["labelBoxBkgColor"] = p.NodeFill,
                ["labelBoxBorderColor"] = p.NodeBorder,
                ["labelTextColor"] = p.NodeText,
                ["loopTextColor"] = p.Text,
                ["activationBkgColor"] = p.ClusterFill,
                ["activationBorderColor"] = p.NodeBorder,
                ["sequenceNumberColor"] = p.Background,

                // Mermaid's own parse-error card. preview-mermaid.js keeps a failed diagram's
                // source on screen instead, but a diagram that fails inside mermaid's renderer
                // after parsing still draws this.
                ["errorBkgColor"] = p.NoteFill,
                ["errorTextColor"] = p.NoteText,

                // Pie. Opacity is pinned to 1 because mermaid otherwise blends each slice toward
                // the canvas at 0.7, which would move every fill off the colour the contrast of the
                // label on it was measured against.
                ["pieOpacity"] = 1,
                ["pieStrokeColor"] = p.SeriesStroke,
                ["pieOuterStrokeColor"] = p.SeriesStroke,
                ["pieSectionTextColor"] = p.SeriesInk,
                ["pieTitleTextColor"] = p.Text,
                ["pieLegendTextColor"] = p.Text,

                // xychart names its series colours in one comma-separated list, and does not read the
                // shared text/line variables for its axes — left unset, its labels and ticks derive
                // to a near-black olive that is invisible on this panel.
                ["xyChart"] = new Dictionary<string, object>
                {
                    ["backgroundColor"] = p.Background,
                    ["titleColor"] = p.Text,
                    ["xAxisLabelColor"] = p.Text,
                    ["xAxisTitleColor"] = p.Text,
                    ["xAxisTickColor"] = p.Line,
                    ["xAxisLineColor"] = p.Line,
                    ["yAxisLabelColor"] = p.Text,
                    ["yAxisTitleColor"] = p.Text,
                    ["yAxisTickColor"] = p.Line,
                    ["yAxisLineColor"] = p.Line,
                    ["plotColorPalette"] = string.Join(", ", p.Series),
                },
            },
        };

        // Slot-numbered categorical variables: pie1…pie12 (slices), git0…git7 with their
        // gitBranchLabel counterparts (branches), and cScale0…cScale11 with cScaleLabel (journey
        // and timeline sections). Filled from the palette in order, then held at the neutral —
        // never wrapped back to the first hue, which would give two categories one colour.
        var vars = (Dictionary<string, object>)config["themeVariables"];
        for (var i = 0; i < 12; i++) vars[$"pie{i + 1}"] = SeriesAt(p, i);
        for (var i = 0; i < 8; i++)
        {
            vars[$"git{i}"] = SeriesAt(p, i);
            vars[$"gitBranchLabel{i}"] = p.SeriesInk;
        }
        for (var i = 0; i < 12; i++)
        {
            vars[$"cScale{i}"] = SeriesAt(p, i);
            vars[$"cScaleLabel{i}"] = p.SeriesInk;
        }

        return JsonSerializer.Serialize(config, ConfigOptions);
    }

    private static string SeriesAt(MermaidPalette p, int index) =>
        index < p.Series.Count ? p.Series[index] : p.SeriesOverflow;

    // The same stack preview.css sets on the document, so a diagram's labels are set in the same
    // face as the prose around them.
    private const string FontStack =
        "-apple-system, BlinkMacSystemFont, \"Segoe UI\", \"Segoe UI Variable\", " +
        "Roboto, \"Helvetica Neue\", Arial, sans-serif";
}
