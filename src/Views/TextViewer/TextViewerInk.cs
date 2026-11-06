using System.Globalization;
using Avalonia.Media;
using HikariHopper.Core;

namespace HikariHopper.Views;

/// <summary>
/// The text viewer's inks and type, from the app's own palette: tags in the explorer's "data" pink, names
/// in brass, strings in the check green, numbers in the channel blue, keywords in the code ink, and
/// comments and punctuation faint. Matches are brass and underlined, as the explorer's search marks names.
/// </summary>
internal static class TextViewerInk
{
    public static readonly IBrush Number = new SolidColorBrush(Color.Parse("#7FAEEE"));
    public static readonly IBrush MatchWash = new SolidColorBrush(Color.Parse("#4DD0A94C"));
    public static readonly IBrush SelectionWash = new SolidColorBrush(Color.FromArgb(0x5C, 0x5E, 0x8B, 0xD0));
    public static readonly TextDecorationCollection Underline = TextDecorations.Underline;

    public static Typeface Typeface { get; } = new(Ui.Theme.MonoFont);

    /// <summary>The zoom steps, as scales of the 12px type on 20px lines.</summary>
    public static IReadOnlyList<double> Zooms { get; } = [0.75, 0.85, 1, 1.15, 1.3, 1.5, 1.75, 2];

    public static IBrush Of(TextKind kind) => kind switch
    {
        TextKind.Tag => Ui.Theme.InkData,
        TextKind.Attribute or TextKind.Key => Ui.Theme.Brass,
        TextKind.String => Ui.Theme.Ok,
        TextKind.Number => Number,
        TextKind.Literal => Ui.Theme.InkCode,
        TextKind.Punctuation or TextKind.Comment => Ui.Theme.TextFaint,
        _ => Ui.Theme.TextRow,
    };

    public static FormattedText Text(string text, double size, IBrush ink) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface, size, ink);

    /// <summary>The advance of one character of the mono face at <paramref name="size"/>.</summary>
    public static double CharWidth(double size) => Text("0000000000", size, Ui.Theme.Text).WidthIncludingTrailingWhitespace / 10;
}
