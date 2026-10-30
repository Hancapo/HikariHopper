using System.Reflection;
using Avalonia;
using Avalonia.Harfbuzz;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;

namespace HikariHopper.Infrastructure;

/// <summary>
/// HarfBuzz shaping with Qt's hinted horizontal metrics: every glyph advance is rounded to a whole
/// pixel before letter spacing is added, the way Qt lays text out on Windows (QFontMetrics reports
/// integer advances there and Qt Quick places glyphs on them). Without it Avalonia's fractional
/// design advances make Archivo labels several pixels narrower than the QML ones (for example
/// "Repair alpha edges" at 12px is 102.4px wide instead of Qt's 108px).
/// </summary>
/// <remarks>
/// <see cref="ITextShaperImpl"/> cannot be implemented by application code, so the shaper is a
/// <see cref="DispatchProxy"/> that forwards to Avalonia's public <see cref="HarfBuzzTextShaper"/>
/// and post-processes the shaped buffer it returns.
/// </remarks>
public class QtTextShaper : DispatchProxy
{
    private readonly HarfBuzzTextShaper inner = new();

    /// <summary>
    /// Makes the Qt-metrics shaper the app's <see cref="TextShaper.Current"/>, which the text
    /// formatter asks for before falling back to the platform shaper.
    /// </summary>
    public static AppBuilder Use(AppBuilder builder) => builder.With(new TextShaper(Create()));

    /// <summary>Creates the shaper.</summary>
    public static ITextShaperImpl Create() => Create<ITextShaperImpl, QtTextShaper>();

    /// <summary>Rounds each glyph advance (without letter spacing) to a whole pixel, half away from zero.</summary>
    public static void RoundAdvances(ShapedBuffer buffer, double letterSpacing)
    {
        for (int index = 0; index < buffer.Length; index++)
        {
            GlyphInfo glyph = buffer[index];
            double advance = Math.Round(glyph.GlyphAdvance - letterSpacing, MidpointRounding.AwayFromZero) + letterSpacing;
            buffer[index] = new GlyphInfo(glyph.GlyphIndex, glyph.GlyphCluster, advance, glyph.GlyphOffset);
        }
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        if (targetMethod.Name == nameof(HarfBuzzTextShaper.ShapeText) && args is [ReadOnlyMemory<char> text, TextShaperOptions options])
        {
            ShapedBuffer buffer = inner.ShapeText(text, options);
            RoundAdvances(buffer, options.LetterSpacing);
            return buffer;
        }
        if (targetMethod.Name == nameof(HarfBuzzTextShaper.CreateTypeface) && args is [Avalonia.Media.GlyphTypeface typeface])
            return inner.CreateTypeface(typeface);
        return targetMethod.Invoke(inner, args);
    }
}
