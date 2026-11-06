using Avalonia.Media;
using HikariHopper.Ui;

namespace HikariHopper.Tests;

/// <summary>
/// The theme's contrast, as WCAG 2 measures it: text readable on what it sits on (4.5:1, or 3:1 for icons), and
/// tabs that stand out from the strip they hang from, also on screens that show dark tones lighter (IPS glow).
/// </summary>
public sealed class ThemeContrastTests
{
    private static double Luminance(IImmutableSolidColorBrush brush)
    {
        static double Channel(byte value)
        {
            double c = value / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        Color color = brush.Color;
        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    private static double Ratio(IImmutableSolidColorBrush a, IImmutableSolidColorBrush b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    public static TheoryData<string, string, double> TextPairs => new()
    {
        { nameof(Theme.Text), nameof(Theme.ChromeBg), 4.5 },
        { nameof(Theme.TextDim), nameof(Theme.ChromeBg), 4.5 },
        { nameof(Theme.TextDim), nameof(Theme.TabStripBg), 4.5 },
        { nameof(Theme.TextRow), nameof(Theme.PanelBg), 4.5 },
        { nameof(Theme.TextDim), nameof(Theme.ListHeaderBg), 4.5 },
        { nameof(Theme.TextFaint), nameof(Theme.PanelBg), 4.5 },
        { nameof(Theme.TextFaint), nameof(Theme.InsetBg), 4.5 },
        { nameof(Theme.TextFaint), nameof(Theme.TabStripBg), 4.5 },
        { nameof(Theme.TextFaint), nameof(Theme.ChromeBg), 3 },
        { nameof(Theme.TextFaint), nameof(Theme.ChromeRaised), 3 },
        { nameof(Theme.SelectionText), nameof(Theme.Selection), 4.5 },
    };

    private static IImmutableSolidColorBrush Token(string name) =>
        (IImmutableSolidColorBrush)typeof(Theme).GetProperty(name)!.GetValue(null)!;

    [Theory]
    [MemberData(nameof(TextPairs))]
    public void TextIsReadableOnWhatItSitsOn(string text, string surface, double needed) =>
        Assert.True(Ratio(Token(text), Token(surface)) >= needed,
            $"{text} on {surface}: {Ratio(Token(text), Token(surface)):0.00}:1, needs {needed}:1");

    [Fact]
    public void FaintTextStaysQuieterThanDimText() =>
        Assert.True(Ratio(Theme.TextDim, Theme.PanelBg) - Ratio(Theme.TextFaint, Theme.PanelBg) >= 1);

    [Fact]
    public void TabsStandOutFromTheirStrip()
    {
        // The active tab is the toolbar's colour; the strip is the window's base.
        Assert.True(Ratio(Theme.ChromeBg, Theme.TabStripBg) >= 1.28, $"{Ratio(Theme.ChromeBg, Theme.TabStripBg):0.00}:1");
        Assert.True(Ratio(Theme.TabBg, Theme.TabStripBg) >= 1.4, $"{Ratio(Theme.TabBg, Theme.TabStripBg):0.00}:1");
    }
}
