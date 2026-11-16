using System.Numerics;

namespace HikariHopper.Rendering.World;

/// <summary>
/// An interior room's ambient, as GTA V lights what's in it: its artificial ambient from above and below, and how much
/// of the natural ambient still reaches it. The room's timecycle modifiers set them (its secondary one, then its
/// primary over it), over the weather's own interior values, which are the same at every hour; a room without
/// modifiers has the weather's, whose interior multiplier is nought. All of it from the game's timecycle files.
/// </summary>
/// <param name="Down">The artificial ambient on what faces up: its colour by its intensity and the interior multiplier.</param>
/// <param name="Up">The artificial ambient on what faces down.</param>
/// <param name="Natural">The natural ambient multiplier.</param>
internal readonly record struct InteriorAmbient(Vector3 Down, Vector3 Up, float Natural)
{
    /// <summary>The ambient of a room with these modifiers (by name hash; 0 for none), from the game's timecycle.</summary>
    public static InteriorAmbient For(uint primary, uint secondary, Core.GameTimecycle timecycle)
    {
        float Get(string name)
        {
            if (primary != 0 && timecycle.Modifiers.TryGetValue(primary, out var first) && first.TryGetValue(name, out float value))
                return value;
            if (secondary != 0 && timecycle.Modifiers.TryGetValue(secondary, out var second) && second.TryGetValue(name, out value))
                return value;
            // The weather's interior values are the same at every hour.
            return timecycle.Weather.TryGetValue(name, out float[]? keyframes) && keyframes.Length > 0 ? keyframes[0] : 0;
        }
        Vector3 Colour(string name) => new(Get(name + "_col_r"), Get(name + "_col_g"), Get(name + "_col_b"));
        float multiplier = Get("artificial_int_ambient_multiplier");
        return new InteriorAmbient(Colour("light_artificial_int_down") * Get("light_artificial_int_down_intensity") * multiplier,
            Colour("light_artificial_int_up") * Get("light_artificial_int_up_intensity") * multiplier, Get("natural_ambient_multiplier"));
    }

    /// <summary>The largest ambient a packed colour holds.</summary>
    internal const float PackedRange = 4;

    /// <summary>A colour as one float the GPU reads exactly: a byte a channel over 0 to <see cref="PackedRange"/>.</summary>
    internal static float Pack(Vector3 colour)
    {
        static uint Byte(float value) => (uint)Math.Clamp(MathF.Round(value / PackedRange * 255), 0, 255);
        return Byte(colour.X) + Byte(colour.Y) * 256 + Byte(colour.Z) * 65536;
    }
}
