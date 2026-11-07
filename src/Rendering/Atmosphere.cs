using System.Numerics;

namespace HikariHopper.Rendering;

/// <summary>
/// The CPU mirror of sky.wgsl's medium (after RedFury's Atmosphere), used to colour direct sunlight: how much of the
/// sun's light gets through the air to the ground, reddening as it nears the horizon. Kilometres.
/// </summary>
internal static class Atmosphere
{
    internal const float BottomRadius = 6360, TopRadius = 6460, RayleighScaleHeight = 8, MieScaleHeight = 1.2f;

    /// <summary>Top-of-atmosphere sun illuminance in the renderer's radiance units, as RedFury's.</summary>
    internal const float SunIlluminance = 5;

    private static readonly Vector3 RayleighScattering = new(.005802f, .013558f, .0331f), OzoneAbsorption = new(.00065f, .001881f, .000085f);
    private const float MieExtinction = .00444f;

    internal static Vector3 Extinction(float height)
    {
        float ozone = Math.Max(0, height < 25 ? height / 15 - 2f / 3 : -height / 15 + 8f / 3);
        return RayleighScattering * MathF.Exp(-height / RayleighScaleHeight) + new Vector3(MieExtinction * MathF.Exp(-height / MieScaleHeight)) + OzoneAbsorption * ozone;
    }

    /// <summary>Transmittance from an altitude in metres to space towards the sun; zero when the planet blocks it.</summary>
    internal static Vector3 SunTransmittance(float altitudeMetres, Vector3 sun)
    {
        sun = Vector3.Normalize(sun);
        var origin = new Vector3(0, 0, BottomRadius + Math.Max(altitudeMetres * .001f, .01f));
        if (Intersect(origin, sun, BottomRadius) >= 0)
            return Vector3.Zero;
        float distance = Intersect(origin, sun, TopRadius);
        if (distance <= 0)
            return Vector3.One;
        const int steps = 64;
        float dt = distance / steps;
        Vector3 depth = Vector3.Zero;
        for (int i = 0; i < steps; i++)
            depth += Extinction((origin + sun * ((i + .5f) * dt)).Length() - BottomRadius) * dt;
        return new(MathF.Exp(-depth.X), MathF.Exp(-depth.Y), MathF.Exp(-depth.Z));
    }

    private static float Intersect(Vector3 origin, Vector3 direction, float radius)
    {
        float b = Vector3.Dot(origin, direction), c = origin.LengthSquared() - radius * radius, discriminant = b * b - c;
        if (discriminant < 0)
            return -1;
        float s = MathF.Sqrt(discriminant), near = -b - s, far = -b + s;
        return near >= 0 ? near : far >= 0 ? far : -1;
    }
}
