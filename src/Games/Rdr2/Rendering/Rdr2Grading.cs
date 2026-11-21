using System.Numerics;
using RedFury.Rpf;
using RedFury.World;
using RedTexture = RedFury.Textures.Texture;
using RedTextureFormat = RedFury.Textures.TextureFormat;

namespace HikariHopper.Rendering;

/// <summary>
/// Red Dead Redemption 2's colour grading, as its post-process grades its frame after the tone map, in the display's
/// sRGB: the levels look-up table (the timecycle's colour correction, shift and desaturation, as
/// PS_LUT3DBlend*WithTonalAdjustments bakes it), then the weather's (textures/lutpostfx.ytd's weather_lut_t*_h*, nine
/// tables over the climate's temperature and humidity, blended as the game's weather colour correction blends them).
/// Each is a 16 by 16 by 16 table laid out as the game's, 256 by 16, the blue's slices side by side.
/// </summary>
internal sealed class Rdr2Grading
{
    /// <summary>A table's texels across and down, and its steps along each colour.</summary>
    public const int Width = 256, Height = 16, Steps = 16;

    internal const string TablesPath = "platform:/textures/lutpostfx.ytd";

    // The weather's tables: by temperature (low, middle, high) and humidity, [temperature + 3 humidity].
    private readonly float[]?[] weather;

    private Rdr2Grading(float[]?[] weather) => this.weather = weather;

    /// <summary>Whether the game's weather tables were read (the middle one at least).</summary>
    public bool HasWeather => weather[4] is not null;

    /// <summary>The game's weather tables, read through <paramref name="sources"/>; null without the dictionary.</summary>
    public static Rdr2Grading? Read(WorldGameSource sources, Rdr2Crypto crypto)
    {
        if (sources.Find(TablesPath) is not [.., var dictionary])
            return null;
        var weather = new float[]?[9];
        string[] bands = ["l", "m", "h"];
        foreach (RedTexture texture in dictionary.Load(crypto, entry => RedFury.Ytd.Ytd.FromEntry(entry)).Textures)
            for (int temperature = 0; temperature < 3; temperature++)
                for (int humidity = 0; humidity < 3; humidity++)
                    if (texture.Name.Equals($"weather_lut_t{bands[temperature]}_h{bands[humidity]}", StringComparison.OrdinalIgnoreCase))
                        weather[temperature + 3 * humidity] = Decode(texture);
        return new Rdr2Grading(weather);
    }

    /// <summary>A grading with the weather tables given (for tests), [temperature + 3 humidity].</summary>
    internal static Rdr2Grading Of(params float[]?[] weather) => new(weather);

    /// <summary>A table's texels as RGBA floats; null for a size or format the game's tables don't come in.</summary>
    internal static float[]? Decode(RedTexture texture)
    {
        if (texture.Width != Width || texture.Height != Height || texture.Mips.Count == 0)
            return null;
        ReadOnlySpan<byte> data = texture.Data.Span[texture.Mips[0].Offset..];
        var texels = new float[Width * Height * 4];
        for (int i = 0; i < Width * Height; i++)
            switch (texture.Format)
            {
                case RedTextureFormat.R32G32B32A32Float:
                    for (int c = 0; c < 4; c++)
                        texels[i * 4 + c] = BitConverter.ToSingle(data[((i * 4 + c) * 4)..]);
                    break;
                case RedTextureFormat.R16G16B16A16Float:
                    for (int c = 0; c < 4; c++)
                        texels[i * 4 + c] = (float)BitConverter.ToHalf(data[((i * 4 + c) * 2)..]);
                    break;
                case RedTextureFormat.R8G8B8A8Unorm:
                    for (int c = 0; c < 4; c++)
                        texels[i * 4 + c] = data[i * 4 + c] / 255f;
                    break;
                case RedTextureFormat.B8G8R8A8Unorm:
                    texels[i * 4] = data[i * 4 + 2] / 255f;
                    texels[i * 4 + 1] = data[i * 4 + 1] / 255f;
                    texels[i * 4 + 2] = data[i * 4] / 255f;
                    texels[i * 4 + 3] = data[i * 4 + 3] / 255f;
                    break;
                default:
                    return null;
            }
        return texels;
    }

    /// <summary>The colour a table's texel stands for, as the game's tables lay it out: red across each slice, green down, blue by slice.</summary>
    public static Vector3 IdentityAt(int x, int y) => new(x % Steps / (Steps - 1f), y / (Steps - 1f), x / Steps / (Steps - 1f));

    /// <summary>
    /// The levels table, as PS_LUT3DBlend*WithTonalAdjustments bakes the timecycle's layer (its brightness nought, its
    /// contrast and gamma one): each colour desaturated towards its luminance by <paramref name="desaturation"/> (one
    /// keeps it), then tinted from the shift's colour in the shadows to the correction's above the shift's cut-off
    /// (luminance over it), the tint fading out over the correction's cut-off below white.
    /// </summary>
    /// <param name="correct">postfx_correct_col and, at least 0.0001, postfx_correct_cutoff.</param>
    /// <param name="shift">postfx_shift_col and postfx_shift_cutoff.</param>
    public static float[] Levels(Vector4 correct, Vector4 shift, float desaturation)
    {
        var texels = new float[Width * Height * 4];
        Vector3 weights = new(.2125f, .7154f, .0721f), shifted = new(shift.X, shift.Y, shift.Z), corrected = new(correct.X, correct.Y, correct.Z);
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                Vector3 colour = IdentityAt(x, y);
                float luminance = Vector3.Dot(colour, weights);
                colour = new Vector3(luminance) + (colour - new Vector3(luminance)) * desaturation;
                float across = shift.W > 0 ? Math.Clamp(luminance / shift.W, 0, 1) : 1;
                Vector3 tinted = colour * Vector3.Lerp(shifted, corrected, across);
                float kept = Math.Clamp((luminance - (1 - correct.W)) / MathF.Max(correct.W, .01f), 0, 1);
                Vector3 graded = Vector3.Clamp(Vector3.Lerp(tinted, colour, kept), Vector3.Zero, Vector3.One);
                int at = (y * Width + x) * 4;
                texels[at] = graded.X;
                texels[at + 1] = graded.Y;
                texels[at + 2] = graded.Z;
                texels[at + 3] = 1;
            }
        return texels;
    }

    /// <summary>
    /// How much of each of the weather's tables the game blends, [temperature + 3 humidity], as its weather colour
    /// correction weighs them: the humidity (0 to 1) and the temperature (in degrees) each on a scale from -1 (at its
    /// least) through nought (its base band) to 1 (its most), the tables either side of the middle by how far along each
    /// is, times <paramref name="strength"/> (the timecycle's postfx_weather_color_correction).
    /// </summary>
    /// <param name="humidityBands">WeatherColorCorrection.Humidity's Min, BaseMin, BaseMax and Max.</param>
    /// <param name="temperatureBands">WeatherColorCorrection.Temperature's Min, BaseMin, BaseMax and Max.</param>
    public static float[] WeatherWeights(float humidity, float temperature, Vector4 humidityBands, Vector4 temperatureBands, float strength)
    {
        static float Along(float value, Vector4 bands)
        {
            float below = Math.Clamp((value - bands.X) / (bands.Y - bands.X), 0, 1);
            float above = Math.Clamp((value - bands.Z) / (bands.W - bands.Z), 0, 1);
            return above - (1 - below);
        }
        float wet = Along(Math.Clamp(humidity, 0, 1), humidityBands), warm = Along(temperature, temperatureBands);
        int h = Math.Sign(wet), t = Math.Sign(warm);
        float hs = MathF.Abs(wet), ts = MathF.Abs(warm);
        var weights = new float[9];
        weights[1 + 3 * (1 + h)] += (1 - ts) * hs * strength;
        weights[4] += (1 - hs) * (1 - ts) * strength;
        weights[1 + t + 3] += (1 - hs) * ts * strength;
        weights[1 + t + 3 * (1 + h)] += hs * ts * strength;
        return weights;
    }

    /// <summary>
    /// The weather's table: its tables blended by <paramref name="weights"/>, the rest of the way the identity, as the
    /// game's table blend (PS_LUT3DBlend*) adds its layers up; weights over one in all are taken over their sum.
    /// </summary>
    public float[] Weather(float[] weights)
    {
        float sum = 0;
        for (int i = 0; i < 9; i++)
            if (weather[i] is not null)
                sum += weights[i];
        float scale = sum > 1 ? 1 / sum : 1, identity = Math.Max(0, 1 - sum * scale);
        var texels = new float[Width * Height * 4];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int at = (y * Width + x) * 4;
                Vector3 colour = IdentityAt(x, y) * identity;
                for (int i = 0; i < 9; i++)
                    if (weather[i] is { } table && weights[i] > 0)
                        colour += new Vector3(table[at], table[at + 1], table[at + 2]) * weights[i] * scale;
                colour = Vector3.Clamp(colour, Vector3.Zero, Vector3.One);
                texels[at] = colour.X;
                texels[at + 1] = colour.Y;
                texels[at + 2] = colour.Z;
                texels[at + 3] = 1;
            }
        return texels;
    }
}
