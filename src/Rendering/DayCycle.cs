using System.Globalization;
using System.Numerics;
using System.Xml;

namespace HikariHopper.Rendering;

/// <summary>Where the directional light shading and shadows use comes from: the sun, or the moon at night.</summary>
/// <param name="Direction">Unit direction towards the light, Z-up.</param>
/// <param name="Sun">True for the sun; false for the moon.</param>
internal readonly record struct KeyLight(Vector3 Direction, bool Sun);

/// <summary>
/// The sun's and moon's paths across the sky, as the game's <c>time.xml</c> sets them (<c>common:/data/levels/gta5/</c>):
/// the tilt of the circle each travels (its roll about the east-west axis), the sun's turn about the vertical (its yaw),
/// and how the moon wobbles above and below its circle. Angles are in radians.
/// </summary>
internal sealed record SkyPath(float SunRoll, float SunYaw, float MoonRoll, float MoonWobbleFrequency, float MoonWobbleAmplitude,
    float MoonWobbleOffset)
{
    private const float Degrees = MathF.PI / 180;

    /// <summary>Both editions' <c>time.xml</c>: <c>sun_roll="122" sun_yaw="0.0"</c>, <c>moon_roll="-122"</c>, wobble 2, 0.2, 0.375.</summary>
    public static readonly SkyPath Game = new(122 * Degrees, 0, -122 * Degrees, 2, 0.2f, 0.375f);

    /// <summary>
    /// A <c>time.xml</c>'s paths, as the game reads them: <c>suninfo</c>'s <c>sun_roll</c> and <c>sun_yaw</c>, and
    /// <c>mooninfo</c>'s <c>moon_roll</c> and <c>moon_wobble_freq</c>, <c>_amp</c> and <c>_offset</c>, each in place of
    /// the game's own where it's given; null where the file isn't XML.
    /// </summary>
    public static SkyPath? Read(byte[] data)
    {
        var document = new XmlDocument { XmlResolver = null };
        try
        {
            using var stream = new MemoryStream(data);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document.Load(reader);
        }
        catch (XmlException)
        {
            return null;
        }
        XmlElement? root = document.DocumentElement;
        if (root is null)
            return null;
        XmlElement? sun = root["suninfo"], moon = root["mooninfo"];
        float Value(XmlElement? element, string name, float fallback, float scale = 1) =>
            element?.GetAttributeNode(name) is { } attribute
            && float.TryParse(attribute.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) && float.IsFinite(value)
                ? value * scale
                : fallback;
        return new SkyPath(
            Value(sun, "sun_roll", Game.SunRoll, Degrees),
            Value(sun, "sun_yaw", Game.SunYaw, Degrees),
            Value(moon, "moon_roll", Game.MoonRoll, Degrees),
            Value(moon, "moon_wobble_freq", Game.MoonWobbleFrequency),
            Value(moon, "moon_wobble_amp", Game.MoonWobbleAmplitude),
            Value(moon, "moon_wobble_offset", Game.MoonWobbleOffset));
    }
}

/// <summary>
/// The viewer's sky, as the game moves it (its sky update, after reading <c>time.xml</c>): in world axes (+X east,
/// +Y north, Z up), the sun on a circle through due east and due west, tilted by the sun's roll, rising at 06:00 and
/// setting at 20:00; and the moon on its own circle, opposite the sun on the viewer's day. Only where the light comes
/// from; its colour and strength are the timecycle's.
/// </summary>
internal static class DayCycle
{
    // The day the moon is drawn for: the game counts days, the moon going round its phases every 55. On day 0 it is
    // full, opposite the sun, at the middle of its wobble.
    private const double MoonDay = 0;

    /// <summary>The viewer's time of day: mid-afternoon, with the sun high but to one side.</summary>
    internal const float DefaultHours = 14.5f;

    /// <summary>
    /// How far round its circle the sun is at <paramref name="hours"/>, as the game reckons it: a quarter turn from
    /// midnight to 06:00, half a turn across the day to 20:00, and the last quarter to midnight.
    /// </summary>
    internal static float SunAngle(float hours)
    {
        float hour = Wrap(hours);
        if (hour < 6)
            return hour / 6 * MathF.PI / 2;
        if (hour < 20)
            return MathF.PI / 2 + (hour - 6) / 14 * MathF.PI;
        return 3 * MathF.PI / 2 + (hour - 20) / 4 * MathF.PI / 2;
    }

    /// <summary>Sun direction at <paramref name="hours"/> after midnight; any finite value wraps to the day.</summary>
    internal static Vector3 SunDirection(float hours, SkyPath? path = null)
    {
        path ??= SkyPath.Game;
        float angle = SunAngle(hours);
        // On the circle, tilted by the roll: east at 06:00, overhead side at noon, west at 20:00 ...
        var onCircle = Vector3.Normalize(new(MathF.Sin(angle), -MathF.Cos(angle) * MathF.Cos(path.SunRoll), -MathF.Cos(angle) * MathF.Sin(path.SunRoll)));
        // ... and turned about the vertical by the yaw.
        float cos = MathF.Cos(path.SunYaw), sin = MathF.Sin(path.SunYaw);
        return Vector3.Normalize(new(cos * onCircle.X - sin * onCircle.Y, cos * onCircle.Y + sin * onCircle.X, onCircle.Z));
    }

    /// <summary>
    /// Moon direction at <paramref name="hours"/>: round a circle tilted by the sun's and moon's rolls together, behind
    /// or ahead of the sun by its phase, and lifted off the circle by its wobble.
    /// </summary>
    internal static Vector3 MoonDirection(float hours, SkyPath? path = null)
    {
        path ??= SkyPath.Game;
        float roll = path.SunRoll + path.MoonRoll;
        var axis = new Vector3(0, MathF.Cos(roll), MathF.Sin(roll));
        float angle = (float)((MoonDay % 55 - 27) / 27) * MathF.PI + SunAngle(hours);
        Vector3 onCircle = new Vector3(MathF.Sin(angle), 0, 0) - MathF.Cos(angle) * axis;
        float wobble = MathF.Sin((float)(MoonDay % 27) * 2 * MathF.PI * path.MoonWobbleFrequency) * path.MoonWobbleAmplitude + path.MoonWobbleOffset;
        return Vector3.Normalize(onCircle + wobble * Vector3.Cross(Vector3.UnitX, axis));
    }

    /// <summary>
    /// The light at <paramref name="hours"/>: the sun while the game shows it, from 04:45 to 21:45 (it fades in over
    /// the first quarter hour and out over the last), and the moon the rest of the night.
    /// </summary>
    internal static KeyLight Evaluate(float hours, SkyPath? path = null)
    {
        if (!float.IsFinite(hours))
            throw new ArgumentOutOfRangeException(nameof(hours));
        float hour = Wrap(hours);
        return hour > 4.75f && hour < 21.75f ? new(SunDirection(hour, path), true) : new(MoonDirection(hour, path), false);
    }

    private static float Wrap(float hours)
    {
        float hour = hours % 24;
        return hour < 0 ? hour + 24 : hour;
    }
}
