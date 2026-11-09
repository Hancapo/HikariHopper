using System.Numerics;

namespace HikariHopper.Rendering;

/// <summary>Where the directional light shading and shadows use comes from: the sun, or the moon at night.</summary>
/// <param name="Direction">Unit direction towards the light, Z-up.</param>
/// <param name="Sun">True for the sun; false for the moon.</param>
internal readonly record struct KeyLight(Vector3 Direction, bool Sun);

/// <summary>
/// The viewer's sky path (after RedFury's DayCycle): a fixed-season solar path in world axes (+X east, +Y north, Z up)
/// and a full moon opposite the sun. Only where the light comes from; its colour and strength are the timecycle's.
/// </summary>
internal static class DayCycle
{
    private const float Latitude = 38 * MathF.PI / 180, Declination = 18 * MathF.PI / 180;
    // Clock time of the sun's highest point (daylight-saving offset): sunrise is about 06:00 and sunset 20:00.
    private const float SolarNoon = 13;
    // The sun is the light until it is this far below the horizon, then the moon: the timecycle's directional light is
    // all but out by then (about 21:00), so the change of direction does not show.
    private static readonly float SunsetHandover = MathF.Sin(-9 * MathF.PI / 180);

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
        return sun.Z >= SunsetHandover ? new(sun, true) : new(MoonDirection(hours), false);
    }
}
