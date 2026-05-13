using Xunit;
using FluentAssertions;
using Spectacle.Accessibility;

namespace Spectacle.Tests;

public class WcagContrastTests
{
    [Fact]
    public void White_on_black_is_21() =>
        WcagContrast.Ratio("#ffffff", "#000000").Should().BeApproximately(21.0, 0.01);

    [Fact]
    public void Black_on_black_is_1() =>
        WcagContrast.Ratio("#000000", "#000000").Should().BeApproximately(1.0, 0.01);

    [Fact]
    public void DarkPlus_body_exceeds_AAA()
    {
        // #d4d4d4 on #1e1e1e is the VS Code Dark+ body pair
        var ratio = WcagContrast.Ratio("#d4d4d4", "#1e1e1e");
        ratio.Should().BeGreaterThan(7.0);
    }

    [Fact]
    public void Throws_on_bad_hex() =>
        FluentActions.Invoking(() => WcagContrast.Ratio("not-a-color", "#000000"))
            .Should().Throw<FormatException>();

    [Fact]
    public void Accepts_uppercase_and_short_form()
    {
        WcagContrast.Ratio("#FFF", "#000").Should().BeApproximately(21.0, 0.01);
    }
}
