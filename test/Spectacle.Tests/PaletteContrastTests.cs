using Xunit;
using FluentAssertions;
using Spectacle.Accessibility;

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
    public void Hc_body_meets_AAA() =>
        WcagContrast.Ratio(HcFg, HcBg).Should().BeGreaterThanOrEqualTo(7.0);

    [Fact]
    public void Hc_link_meets_AA() =>
        WcagContrast.Ratio(HcLink, HcBg).Should().BeGreaterThanOrEqualTo(4.5);
}
