using System.Numerics;

namespace HikariHopper.Rendering;

/// <summary>The directional light the sky, shadows and shading use: the sun, or the moon at night.</summary>
/// <param name="Direction">Unit direction towards the light, Z-up.</param>
/// <param name="Illuminance">Top-of-atmosphere illuminance per channel, in the renderer's radiance units.</param>
/// <param name="Sun">True for the sun; false for the moon.</param>
internal readonly record struct KeyLight(Vector3 Direction, Vector3 Illuminance, bool Sun);

/// <summary>
/// The viewer's day (after RedFury's DayCycle): a fixed-season solar path in world axes (+X east, +Y north, Z up) and
/// a full moon opposite the sun. An artistic model, not the game's timecycle.
/// </summary>
internal static class DayCycle
{
    private const float Latitude = 38 * MathF.PI / 180, Declination = 18 * MathF.PI / 180;
    // Clock time of the sun's highest point (daylight-saving offset): sunrise is about 06:00 and sunset 20:00.
    private const float SolarNoon = 13;
    // The sun lights the sky until it is this far below the horizon; the moon then takes over and brightens to full.
    private static readonly float SunsetHandover = MathF.Sin(-9 * MathF.PI / 180), MoonFull = MathF.Sin(-16 * MathF.PI / 180);
    // A bright, cool artistic moon: real moonlight is about 1/400000 of sunlight, below half-float precision.
    internal static readonly Vector3 MoonIlluminance = Atmosphere.SunIlluminance / 400 * new Vector3(.62f, .78f, 1);

    /// <summary>The viewer's time of day: mid-afternoon, with the sun high but to one side.</summary>
    internal const float DefaultHours = 14.5f;

    /// <summary>Sun direction at <paramref name="hours"/> after midnight; any finite value wraps to the day.</summary>
    internal static Vector3 SunDirection(float hours) => Celestial(hours, Declination);

    internal static Vector3 MoonDirection(float hours) => Celestial(hours + 12, -Declination);

    private static Vector3 Celestial(float hours, float declination)
    {
        float hourAngle = (hours % 24 - SolarNoon) * MathF.PI / 12;
        float east = -MathF.Cos(declination) * MathF.Sin(hourAngle);
        float north = MathF.Cos(Latitude) * MathF.Sin(declination) - MathF.Sin(Latitude) * MathF.Cos(declination) * MathF.Cos(hourAngle);
        float up = MathF.Sin(Latitude) * MathF.Sin(declination) + MathF.Cos(Latitude) * MathF.Cos(declination) * MathF.Cos(hourAngle);
        return Vector3.Normalize(new(east, north, up));
    }

    internal static KeyLight Evaluate(float hours)
    {
        if (!float.IsFinite(hours))
            throw new ArgumentOutOfRangeException(nameof(hours));
        Vector3 sun = SunDirection(hours);
        if (sun.Z >= SunsetHandover)
            return new(sun, new(Atmosphere.SunIlluminance), true);
        // Twilight has faded to near black at the handover, so the moon fades in from zero without a visible jump.
        return new(MoonDirection(hours), MoonIlluminance * Smooth(SunsetHandover, MoonFull, sun.Z), false);
    }

    private static float Smooth(float from, float to, float value)
    {
        float t = Math.Clamp((value - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }
}
