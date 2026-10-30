using Avalonia.Data.Converters;
using Avalonia.Media;

namespace HikariHopper.Views.Controls;

/// <summary>
/// Qt's line box for a font: <c>round(ascent) + round(descent)</c> pixels (QML Text
/// <c>implicitHeight</c> on Windows), where Avalonia uses the unrounded sum. An app-wide
/// TextBlock style applies it as <see cref="TextBlock.LineHeight"/>, so stacked labels keep the
/// QML spacing instead of drifting by a pixel per line at sizes such as Space Mono 9 and 12.
/// </summary>
public static class QtTextMetrics
{
    private static readonly Dictionary<(FontFamily, double, FontWeight, FontStyle), double> Cache = new();

    /// <summary>Converts (FontFamily, FontSize, FontWeight, FontStyle) to the Qt line height.</summary>
    public static IMultiValueConverter LineHeightConverter { get; } = new FuncMultiValueConverter<object?, double>(values =>
    {
        var list = values.ToList();
        if (list is not [FontFamily family, double size, FontWeight weight, FontStyle style])
            return double.NaN;
        return LineHeight(family, size, weight, style);
    });

    public static double LineHeight(FontFamily family, double size, FontWeight weight = FontWeight.Normal, FontStyle style = FontStyle.Normal)
    {
        var key = (family, size, weight, style);
        if (Cache.TryGetValue(key, out double cached))
            return cached;
        double height = double.NaN;
        if (FontManager.Current.TryGetGlyphTypeface(new Typeface(family, style, weight), out GlyphTypeface? glyphs))
        {
            FontMetrics metrics = glyphs.Metrics;
            double scale = size / metrics.DesignEmHeight;
            height = Math.Round(-metrics.Ascent * scale, MidpointRounding.AwayFromZero)
                + Math.Round(metrics.Descent * scale, MidpointRounding.AwayFromZero);
        }
        Cache[key] = height;
        return height;
    }
}
