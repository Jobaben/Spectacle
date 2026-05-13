using System.Globalization;

namespace Spectacle.Accessibility;

public static class WcagContrast
{
    public static double Ratio(string fgHex, string bgHex)
    {
        var l1 = RelativeLuminance(fgHex);
        var l2 = RelativeLuminance(bgHex);
        var (lighter, darker) = l1 >= l2 ? (l1, l2) : (l2, l1);
        return (lighter + 0.05) / (darker + 0.05);
    }

    public static double RelativeLuminance(string hex)
    {
        var (r, g, b) = ParseHex(hex);
        double lin(int c)
        {
            var s = c / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * lin(r) + 0.7152 * lin(g) + 0.0722 * lin(b);
    }

    private static (int r, int g, int b) ParseHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex) || hex[0] != '#')
            throw new FormatException($"Expected #RGB or #RRGGBB, got '{hex}'.");
        var body = hex[1..];
        if (body.Length == 3) body = string.Concat(body.Select(c => $"{c}{c}"));
        if (body.Length != 6 || !body.All(IsHex))
            throw new FormatException($"Expected #RGB or #RRGGBB, got '{hex}'.");
        return (
            int.Parse(body[0..2], NumberStyles.HexNumber),
            int.Parse(body[2..4], NumberStyles.HexNumber),
            int.Parse(body[4..6], NumberStyles.HexNumber));
    }

    private static bool IsHex(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
}
