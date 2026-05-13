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
}
