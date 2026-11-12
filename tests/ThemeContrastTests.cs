using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using HikariHopper.Ui;

namespace HikariHopper.Tests;

/// <summary>
/// The theme's contrast across the app, as WCAG 2 measures it: text readable on every surface it sits on (4.5:1;
/// 3:1 for marks and icons), and neighbouring surfaces far enough apart in lightness to tell apart also on screens
/// that show dark tones lighter (IPS glow).
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
        double la = Luminance(Over(a, b)), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary><paramref name="ink"/> as it shows over <paramref name="surface"/>, its alpha blended in.</summary>
    private static IImmutableSolidColorBrush Over(IImmutableSolidColorBrush ink, IImmutableSolidColorBrush surface)
    {
        Color a = ink.Color, b = surface.Color;
        double alpha = a.A / 255.0;
        byte Mix(byte x, byte y) => (byte)Math.Round(x * alpha + y * (1 - alpha));
        return new ImmutableSolidColorBrush(Color.FromRgb(Mix(a.R, b.R), Mix(a.G, b.G), Mix(a.B, b.B)));
    }

    /// <summary>CIE lightness, 0 to 100: how far apart two surfaces look.</summary>
    private static double Lightness(IImmutableSolidColorBrush brush)
    {
        double y = Luminance(brush);
        return 116 * (y > 0.008856 ? Math.Cbrt(y) : 7.787 * y + 16.0 / 116) - 16;
    }

    /// <summary>Fails with every pair that doesn't hold, not only the first.</summary>
    private static void AllHold(IEnumerable<string?> failures)
    {
        string[] failed = [.. failures.OfType<string>()];
        Assert.True(failed.Length == 0, string.Join('\n', failed));
    }

    private static List<(string Text, string Surface, double Needed)> TextPairs()
    {
        var pairs = new List<(string, string, double)>();
        // Text of every weight on every surface it is used on.
        string[] surfaces = [nameof(Theme.PanelBg), nameof(Theme.ChromeBg), nameof(Theme.InsetBg), nameof(Theme.TabStripBg),
            nameof(Theme.ListHeaderBg), nameof(Theme.HoverBg), nameof(Theme.WindowBg)];
        foreach (string text in new[] { nameof(Theme.Text), nameof(Theme.TextRow), nameof(Theme.TextDim), nameof(Theme.TextFaint) })
            foreach (string surface in surfaces)
                pairs.Add((text, surface, 4.5));
        // Raised and hovered chrome (menus, buttons, tabs): all but faint text, which there is only for what is off.
        foreach (string surface in new[] { nameof(Theme.ChromeRaised), nameof(Theme.HoverChrome) })
            foreach (string text in new[] { nameof(Theme.Text), nameof(Theme.TextRow), nameof(Theme.TextDim) })
                pairs.Add((text, surface, 4.5));
        pairs.Add((nameof(Theme.TextFaint), nameof(Theme.ChromeRaised), 4));
        // Status inks where they are written.
        foreach (string ink in new[] { nameof(Theme.Error), nameof(Theme.Brass), nameof(Theme.Ok), nameof(Theme.Warning) })
            foreach (string surface in new[] { nameof(Theme.PanelBg), nameof(Theme.ChromeBg) })
                pairs.Add((ink, surface, 4.5));
        // Marks and icons: 3:1.
        foreach (string ink in new[] { nameof(Theme.Accent), nameof(Theme.InkCode), nameof(Theme.InkAsset), nameof(Theme.InkData) })
            foreach (string surface in new[] { nameof(Theme.PanelBg), nameof(Theme.ChromeBg), nameof(Theme.ChromeRaised), nameof(Theme.HoverChrome) })
                pairs.Add((ink, surface, 3));
        pairs.Add((nameof(Theme.SelectionText), nameof(Theme.Selection), 4.5));
        pairs.Add((nameof(Theme.SelectionInk), nameof(Theme.Selection), 4.5));
        pairs.Add((nameof(Theme.SelectionFaint), nameof(Theme.Selection), 4.5));
        return pairs;
    }

    // On the UI thread: the viewer's inks are made on first use, and they belong to the thread that makes them.
    [AvaloniaFact]
    public void TheTextViewersInksReadOnItsSelectionAndItsLines()
    {
        var selection = new ImmutableSolidColorBrush(((ISolidColorBrush)Views.TextViewerInk.SelectionWash).Color);
        // Each kind of text it colours, selected, and unselected on the page and on the current line.
        AllHold(
            from kind in Enum.GetValues<Core.TextKind>()
            let text = new ImmutableSolidColorBrush(((ISolidColorBrush)Views.TextViewerInk.Of(kind)).Color)
            from surface in new[] { selection, Theme.AppBg, Theme.HoverBg }
            select Ratio(text, surface) >= 4.5 ? null : $"{kind} on {surface.Color}: {Ratio(text, surface):0.00}:1");
    }

    private static readonly (string A, string B, double Gap)[] NeighbourPairs =
    [
        (nameof(Theme.ChromeBg), nameof(Theme.TabStripBg), 12),      // the active tab and the title bar vs the strip
        (nameof(Theme.PanelBg), nameof(Theme.WindowBg), 5.5),        // the list panel vs the base it floats on
        (nameof(Theme.ListHeaderBg), nameof(Theme.PanelBg), 6),      // the column header vs the list
        (nameof(Theme.InsetBg), nameof(Theme.ChromeBg), 10),         // fields and wells vs the toolbar
        (nameof(Theme.InsetBg), nameof(Theme.PanelBg), 4),           // fields vs the list's colour (dialogs, viewers)
        (nameof(Theme.ChromeRaised), nameof(Theme.InsetBg), 12),     // a raised segment vs its well
        (nameof(Theme.HoverBg), nameof(Theme.PanelBg), 7),           // a hovered row
        (nameof(Theme.TabBg), nameof(Theme.TabStripBg), 14),        // a hovered tab vs the strip
    ];

    private static IImmutableSolidColorBrush Token(string name) =>
        (IImmutableSolidColorBrush)typeof(Theme).GetProperty(name)!.GetValue(null)!;

    [Fact]
    public void TextIsReadableOnWhatItSitsOn() =>
        AllHold(TextPairs().Select(pair => Ratio(Token(pair.Text), Token(pair.Surface)) >= pair.Needed ? null
            : $"{pair.Text} on {pair.Surface}: {Ratio(Token(pair.Text), Token(pair.Surface)):0.00}:1, needs {pair.Needed}:1"));

    [Fact]
    public void NeighbouringSurfacesAreFarEnoughApart() =>
        AllHold(NeighbourPairs.Select(pair => Math.Abs(Lightness(Token(pair.A)) - Lightness(Token(pair.B))) >= pair.Gap ? null
            : $"{pair.A} vs {pair.B}: {Math.Abs(Lightness(Token(pair.A)) - Lightness(Token(pair.B))):0.0} apart in lightness, needs {pair.Gap}"));

    [Fact]
    public void TheTextWeightsStayInOrder()
    {
        // Each step of the ladder is visibly quieter than the one above it.
        IImmutableSolidColorBrush[] ladder = [Theme.Text, Theme.TextRow, Theme.TextDim, Theme.TextFaint];
        for (int step = 1; step < ladder.Length; step++)
            Assert.True(Lightness(ladder[step - 1]) - Lightness(ladder[step]) >= 6, $"step {step}");
    }
}
