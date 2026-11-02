using Avalonia.Media;

namespace HikariHopper.Views.Controls;

/// <summary>
/// Qt's line box for a font: <c>round(ascent) + round(descent)</c> pixels (QML Text
/// <c>implicitHeight</c> on Windows), where Avalonia uses the unrounded sum. Text fields use it to
/// centre their single line.
/// </summary>
public static class QtTextMetrics
{
    private static readonly Dictionary<(FontFamily, double, FontWeight, FontStyle), double> Cache = new();

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
