using System.Numerics;

namespace HikariHopper.Rendering;

/// <summary>
/// A Z-up camera that orbits a centre and zooms towards it (after RedFury's), or flies freely: then it turns about
/// its eye and moves, and its centre is the point a fixed distance ahead, which it orbits again once back. System.Numerics matrices are row-vector
/// and upload as-is: WGSL reads them column-major, so <c>matrix * vector</c> there is <c>vector * matrix</c> here.
/// </summary>
internal sealed class OrbitCamera
{
    public const float FieldOfView = 55 * MathF.PI / 180;

    public static readonly float TanHalf = MathF.Tan(FieldOfView / 2);

    /// <summary>The furthest the near plane is, in metres.</summary>
    public const float NearPlane = .005f;

    public Vector3 Center { get; private set; }

    /// <summary>The size of what is framed, which sets the zoom limits and how fast the free camera flies.</summary>
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

    /// <summary>Whether it flies: a drag turns it about its eye, and it moves at <see cref="Speed"/>.</summary>
    public bool Free { get; set; }

    /// <summary>How fast it flies, in metres a second; set from what is framed, and by the wheel while it flies.</summary>
    public float Speed { get; private set; } = 2;

    /// <summary>Looks at a sphere around <paramref name="center"/> from a distance that shows it whole.</summary>
    public void Frame(Vector3 center, float radius)
    {
        if (!float.IsFinite(radius) || radius <= 0)
            throw new ArgumentOutOfRangeException(nameof(radius));
        Center = center;
        Radius = radius;
        Distance = radius * 2.8f;
        Speed = Math.Max(radius, .5f) * 1.5f;
    }

    /// <summary>Turns about the eye by a drag in pixels, as <see cref="Orbit"/> turns about the centre: dragging right looks right.</summary>
    public void Look(float dx, float dy)
    {
        Vector3 eye = Eye;
        Orbit(dx, dy);
        Center += eye - Eye;
    }

    /// <summary>
    /// Flies for <paramref name="seconds"/> at <see cref="Speed"/> (times <paramref name="boost"/>): forward along the
    /// view, right, and up in the world, each -1 to 1.
    /// </summary>
    public void Fly(float forward, float right, float up, float seconds, float boost = 1)
    {
        Vector3 direction = Forward * forward + Right * right + Vector3.UnitZ * up;
        if (direction.LengthSquared() > 1)
            direction = Vector3.Normalize(direction);
        Center += direction * (Speed * boost * seconds);
    }

    /// <summary>Flies faster for positive wheel steps, slower for negative.</summary>
    public void Accelerate(float steps) => Speed = Math.Clamp(Speed * MathF.Exp(steps * .2f), Radius * .02f, Math.Max(Radius, 1) * 200);

    /// <summary>Turns around the centre by a drag in pixels; dragging up raises the camera.</summary>
    public void Orbit(float dx, float dy)
    {
        Yaw = MathF.IEEERemainder(Yaw - dx * .006f, MathF.Tau);
        Pitch = Math.Clamp(Pitch + dy * .006f, -1.5f, 1.5f);
    }

    /// <summary>
    /// Turns to look from along a world axis (+X, -Y, +Z…), as a 3D tool's axis gizmo does: from the side, level; from
    /// above or below, as near straight as the camera goes, with +Y up the screen. Orbiting, it looks at the centre from
    /// there; flying, the eye stays where it is and looks the other way along the axis.
    /// </summary>
    public void LookFrom(Vector3 axis)
    {
        Vector3 eye = Eye;
        if (MathF.Abs(axis.Z) > MathF.Abs(axis.X) && MathF.Abs(axis.Z) > MathF.Abs(axis.Y))
        {
            Yaw = -MathF.PI / 2;
            Pitch = MathF.Sign(axis.Z) * 1.5f;
        }
        else
        {
            Yaw = MathF.Atan2(axis.Y, axis.X);
            Pitch = 0;
        }
        if (Free)
            Center += eye - Eye;
    }

    /// <summary>Moves closer for positive wheel steps, further for negative.</summary>
    public void Zoom(float steps) => Distance = Math.Clamp(Distance * MathF.Exp(-steps * .12f), Radius * .02f, Radius * 200);

    /// <summary>
    /// The view and an infinite reversed-Z projection (depth is near over distance, zero at infinity), for positions
    /// relative to <paramref name="origin"/>: content far out in the world stays precise. The near plane is a few
    /// millimetres whatever the size of what is framed, so the camera can come right up to a surface; reversed Z in a
    /// 32-bit float depth keeps the rest precise. It is closer still when the camera is that close to the centre.
    /// </summary>
    public Matrix4x4 ViewProjection(float aspect, Vector3 origin = default)
    {
        Vector3 eye = Eye - origin;
        float near = Math.Clamp(Distance * .002f, .0001f, NearPlane);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(FieldOfView, aspect, near, near * 2);
        projection.M33 = 0;
        projection.M43 = near;
        return Matrix4x4.CreateLookAt(eye, eye + Forward, Up) * projection;
    }
}
