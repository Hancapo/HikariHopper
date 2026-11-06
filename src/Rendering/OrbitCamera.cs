using System.Numerics;

namespace HikariHopper.Rendering;

/// <summary>
/// A Z-up camera that orbits a centre and zooms towards it (after RedFury's). System.Numerics matrices are row-vector
/// and upload as-is: WGSL reads them column-major, so <c>matrix * vector</c> there is <c>vector * matrix</c> here.
/// </summary>
internal sealed class OrbitCamera
{
    public const float FieldOfView = 55 * MathF.PI / 180;

    public static readonly float TanHalf = MathF.Tan(FieldOfView / 2);

    public Vector3 Center { get; private set; }

    /// <summary>The size of what is framed, which sets the zoom limits and the near plane.</summary>
    public float Radius { get; private set; } = 1;

    public float Distance { get; private set; } = 3;

    /// <summary>Azimuth in radians.</summary>
    public float Yaw { get; private set; } = -1;

    /// <summary>Elevation in radians, kept away from the poles.</summary>
    public float Pitch { get; private set; } = .35f;

    public Vector3 Eye => Center + new Vector3(MathF.Cos(Pitch) * MathF.Cos(Yaw), MathF.Cos(Pitch) * MathF.Sin(Yaw), MathF.Sin(Pitch)) * Distance;

    public Vector3 Forward => Vector3.Normalize(Center - Eye);

    public Vector3 Right => Vector3.Normalize(Vector3.Cross(Forward, Vector3.UnitZ));

    public Vector3 Up => Vector3.Cross(Right, Forward);

    /// <summary>Looks at a sphere around <paramref name="center"/> from a distance that shows it whole.</summary>
    public void Frame(Vector3 center, float radius)
    {
        if (!float.IsFinite(radius) || radius <= 0)
            throw new ArgumentOutOfRangeException(nameof(radius));
        Center = center;
        Radius = radius;
        Distance = radius * 2.8f;
    }

    /// <summary>Turns around the centre by a drag in pixels; dragging up raises the camera.</summary>
    public void Orbit(float dx, float dy)
    {
        Yaw = MathF.IEEERemainder(Yaw - dx * .006f, MathF.Tau);
        Pitch = Math.Clamp(Pitch + dy * .006f, -1.5f, 1.5f);
    }

    /// <summary>Moves closer for positive wheel steps, further for negative.</summary>
    public void Zoom(float steps) => Distance = Math.Clamp(Distance * MathF.Exp(-steps * .12f), Radius * .02f, Radius * 200);

    /// <summary>
    /// The view and an infinite reversed-Z projection (depth is near over distance, zero at infinity), for positions
    /// relative to <paramref name="origin"/>: content far out in the world stays precise.
    /// </summary>
    public Matrix4x4 ViewProjection(float aspect, Vector3 origin = default)
    {
        Vector3 eye = Eye - origin;
        float near = Math.Max(.0001f, Math.Min(Distance * .01f, Radius * .005f));
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(FieldOfView, aspect, near, near * 2);
        projection.M33 = 0;
        projection.M43 = near;
        return Matrix4x4.CreateLookAt(eye, eye + Forward, Up) * projection;
    }
}
