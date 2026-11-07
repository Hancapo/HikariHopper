using System.Numerics;
using Avalonia.Media;
using T = HikariHopper.Ui.Theme;

namespace HikariHopper.Rendering;

/// <summary>
/// The 3D viewport's backdrop, in the app's own colours: the window's base at the zenith fading to the chrome's
/// colour at the horizon, the hard seams' colour for the void below it, grey grid lines, and the X and Y axes in the
/// error's red and the success green.
/// </summary>
internal static class ViewportPalette
{
    /// <summary>The colours as the shader takes them: linear RGB, with how strongly a line shows in alpha.</summary>
    public static Vector4[] Linear() =>
    [
        ToLinear(T.WindowBg.Color),     // zenith
        ToLinear(T.ChromeBg.Color),     // horizon
        ToLinear(T.WindowChrome.Color), // void
        ToLinear(T.ChromeBg.Color),     // minor grid lines
        ToLinear(T.Border.Color),       // major grid lines
        ToLinear(T.Error.Color),        // X axis
        ToLinear(T.Ok.Color),           // Y axis
    ];

    /// <summary>An sRGB colour in linear light, keeping its alpha.</summary>
    internal static Vector4 ToLinear(Color colour)
    {
        static float Channel(byte value)
        {
            float c = value / 255f;
            return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
        }
        return new Vector4(Channel(colour.R), Channel(colour.G), Channel(colour.B), colour.A / 255f);
    }
}
