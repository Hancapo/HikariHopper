using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace HikariHopper.Views;

/// <summary>Qt elision modes Avalonia's TextTrimming lacks (QTextEngine::elidedText).</summary>
public static class QtElide
{
    private const string Ellipsis = "…";

    /// <summary>
    /// <c>Text.ElideMiddle</c>: characters are taken alternately from both ends while they fit next to
    /// the ellipsis, as QTextEngine::elidedText does.
    /// </summary>
    public static string Middle(string text, FontFamily family, double size, double width)
    {
        var typeface = new Typeface(family);
        if (Measure(text, typeface, size) <= width)
            return text;
        double available = width - Measure(Ellipsis, typeface, size);
        double leftWidth = 0;
        double rightWidth = 0;
        int leftPos = 0;
        int rightPos = text.Length;
        int nextLeft = 0;
        int nextRight = text.Length;
        do
        {
            leftPos = nextLeft;
            rightPos = nextRight;
            ++nextLeft;
            leftWidth += Measure(text[leftPos..nextLeft], typeface, size);
            --nextRight;
            rightWidth += Measure(text[nextRight..rightPos], typeface, size);
        }
        while (nextLeft < text.Length && nextRight > 0 && leftWidth + rightWidth < available);
        return text[..leftPos] + Ellipsis + text[rightPos..];
    }

    private static double Measure(string text, Typeface typeface, double size)
    {
        using var layout = new TextLayout(text, typeface, size, Brushes.Black);
        return layout.WidthIncludingTrailingWhitespace;
    }
}
