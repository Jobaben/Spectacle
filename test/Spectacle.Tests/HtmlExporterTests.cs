using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class HtmlExporterTests
{
    [Fact]
    public void Produces_a_complete_standalone_document()
    {
        var html = HtmlExporter.FromMarkdown("# Title\n\nHello world.", PreviewTheme.Dark, "My Doc");

        html.Should().StartWith("<!DOCTYPE html>");
        html.Should().Contain("<title>My Doc</title>");
        html.Should().Contain("<h1");
        html.Should().Contain("Hello world.");
    }

    [Fact]
    public void Inlines_styles_and_omits_external_base_href()
    {
        var html = HtmlExporter.FromMarkdown("# Title", PreviewTheme.Dark, "Doc");

        // Self-contained: styles are inlined and there is no <base> pointing at the
        // live-preview virtual host.
        html.Should().Contain("<style>");
        html.Should().NotContain("<base ");
    }

    [Fact]
    public void Drops_live_preview_host_scripting()
    {
        var html = HtmlExporter.FromMarkdown("# Title\n\ntext", PreviewTheme.Dark, "Doc");

        html.Should().NotContain("__spectacleAnnotations__");
        html.Should().NotContain("__spectacleOutline__");
    }

    [Fact]
    public void Escapes_the_title()
    {
        var html = HtmlExporter.FromMarkdown("x", PreviewTheme.Dark, "A & B <x>");

        html.Should().Contain("<title>A &amp; B &lt;x&gt;</title>");
    }

    [Fact]
    public void Theme_selection_changes_the_embedded_stylesheet()
    {
        var dark = HtmlExporter.FromMarkdown("# T", PreviewTheme.Dark, "Doc");
        var hc = HtmlExporter.FromMarkdown("# T", PreviewTheme.HighContrast, "Doc");

        dark.Should().NotBe(hc);
    }
}
