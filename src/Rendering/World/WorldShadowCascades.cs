using System.Numerics;

namespace HikariHopper.Rendering.World;

/// <summary>
/// One orthographic view along the sun, in render space (the world less the scene's origin, the camera), with standard
/// 0 to 1 depth (RedFury's ShadowCascade, copied).
/// </summary>
internal readonly record struct ShadowCascade(Matrix4x4 View, Matrix4x4 ViewProjection, Vector3 Center, float Radius, float Depth, float Split, float TexelSize)
{
    /// <summary>Whether a sphere may cast into it: anything between the light's plane and the far side of its slice.</summary>
    internal bool Intersects(Vector3 center, float radius)
    {
        Vector3 p = Vector3.Transform(center, View);
        float extent = Radius + radius;
        return MathF.Abs(p.X) <= extent && MathF.Abs(p.Y) <= extent && -p.Z + radius >= 0 && -p.Z - radius <= Depth;
    }

    /// <summary>
    /// The cascade as seen from an origin <paramref name="delta"/> away from the one it was rendered at: a point now at p
    /// was at p + delta then.
    /// </summary>
    internal ShadowCascade Relocate(Vector3 delta)
    {
        if (delta == Vector3.Zero)
            return this;
        Matrix4x4 shift = Matrix4x4.CreateTranslation(delta);
        return this with { View = shift * View, ViewProjection = shift * ViewProjection, Center = Center - delta };
    }
}

/// <summary>
/// The sun's shadow over the world as RedFury's world viewer casts it (its ShadowCascades and shadow schedule, copied):
/// four slices of the view out to 500 m, split between logarithmic and even, each in the smallest sphere round its slice
/// (which doesn't change as the camera turns), snapped to whole texels in world space. Casters as far as 1.5 km
/// towards the sun cast in.
/// </summary>
internal static class WorldShadowCascades
{
    internal const int Count = 4;

    // The camera's near plane, where the shadow starts and ends, and how far towards the sun casters are taken in.
    internal const float Near = .5f, ShadowNear = .5f, ShadowFar = 500, CasterDepth = 1500;

    /// <summary>The practical split scheme: a blend of logarithmic and even distances, ending at far.</summary>
    internal static float[] Splits(float near, float far, float lambda = .8f)
    {
        if (!(near > 0) || !(far > near) || !float.IsFinite(far))
            throw new ArgumentOutOfRangeException(nameof(far));
        var result = new float[Count];
        for (int i = 1; i <= Count; i++)
        {
            float t = (float)i / Count;
            result[i - 1] = lambda * near * MathF.Pow(far / near, t) + (1 - lambda) * (near + (far - near) * t);
        }
        result[^1] = far;
        return result;
    }

    /// <summary>The cascades for a camera at <paramref name="eye"/> (render space's origin) looking along <paramref name="forward"/>.</summary>
    /// <param name="resolution">The shadow map's size, in texels.</param>
    internal static ShadowCascade[] Compute(Vector3 eye, Vector3 forward, float aspect, Vector3 sun, int resolution)
    {
        sun = Vector3.Normalize(sun);
        float[] splits = Splits(ShadowNear, ShadowFar);
        Vector3 lightForward = -sun;
        Vector3 hint = MathF.Abs(sun.Z) > .99f ? Vector3.UnitY : Vector3.UnitZ;
        Vector3 lightRight = Vector3.Normalize(Vector3.Cross(lightForward, hint));
        Vector3 lightUp = Vector3.Cross(lightRight, lightForward);
        float k2 = OrbitCamera.TanHalf * OrbitCamera.TanHalf * (1 + aspect * aspect);
        var result = new ShadowCascade[Count];
        for (int c = 0; c < Count; c++)
        {
            float n = c == 0 ? Near : splits[c - 1], f = splits[c], distance, radius;
            // The smallest sphere round the slice of the view: the same whichever way the camera turns.
            if (k2 >= (f - n) / (f + n))
            {
                distance = f;
                radius = f * MathF.Sqrt(k2);
            }
            else
            {
                distance = .5f * (f + n) * (1 + k2);
                radius = .5f * MathF.Sqrt((f - n) * (f - n) + 2 * (f * f + n * n) * k2 + (f + n) * (f + n) * k2 * k2);
            }
            radius = MathF.Ceiling(radius * 64) / 64;
            float texel = 2 * radius / resolution;
            // Snapped in the world itself (in double precision): an origin that follows the camera mustn't move the grid.
            double ax = (double)eye.X + (double)forward.X * distance, ay = (double)eye.Y + (double)forward.Y * distance, az = (double)eye.Z + (double)forward.Z * distance;
            double cx = ax * lightRight.X + ay * lightRight.Y + az * lightRight.Z, cy = ax * lightUp.X + ay * lightUp.Y + az * lightUp.Z;
            float dx = (float)(Math.Round(cx / texel) * texel - cx), dy = (float)(Math.Round(cy / texel) * texel - cy);
            Vector3 center = forward * distance + lightRight * dx + lightUp * dy;
            float depth = 2 * radius + CasterDepth;
            Matrix4x4 lightView = Matrix4x4.CreateLookAt(center + sun * (radius + CasterDepth), center, lightUp);
            Matrix4x4 projection = Matrix4x4.CreateOrthographic(2 * radius, 2 * radius, 0, depth);
            result[c] = new ShadowCascade(lightView, lightView * projection, center, radius, depth, f, texel);
        }
        return result;
    }

    /// <summary>
    /// Whether a stale cascade is rendered in this frame's slot: the nearest every frame, the second every other, the far
    /// pair alternating every fourth, so at most two a frame.
    /// </summary>
    internal static bool Due(int cascade, long slot) => cascade switch
    {
        0 => true,
        1 => slot % 2 == 1,
        2 => slot % 4 == 0,
        _ => slot % 4 == 2,
    };
}
