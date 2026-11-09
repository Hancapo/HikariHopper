using System.Numerics;
using FiveFury.Bounds;
using FiveFury.Drawables;
using FiveFury.Extensions;

namespace HikariHopper.Rendering;

/// <summary>
/// What the 3D viewer can draw over a drawable, through it: its skeleton, each bone to its parent with a small cross
/// at each joint; its collision (<see cref="Solid"/>); and its lights, a cross where each shines from in its colour,
/// with how far it reaches (a sphere's three circles for a point light, a cone for a spot). Segments are relative to
/// the scene's origin.
/// </summary>
internal sealed record DrawableOverlays(ViewportSegment[] Skeleton, ViewportSegment[] Lights)
{
    public static readonly DrawableOverlays None = new([], []);

    /// <summary>The collision as the collision viewer draws it: its triangles in their materials' colours, with their edges.</summary>
    public ViewportVertex[] Solid { get; init; } = [];

    internal static readonly uint BoneColour = ViewportMesh.Pack(150, 200, 255);
    private const float Width = 1.5f;
    private const int CircleSegments = 32;

    // The game's light types.
    private const byte SpotLight = 2;

    /// <summary>
    /// The overlays of placed drawables, their crosses sized for a scene of <paramref name="radius"/>. A fragment's
    /// collision is its physics composite, <paramref name="fragmentBound"/>, in place of its drawables' own, and its own
    /// lights, <paramref name="fragmentLights"/>, move with its main drawable's bones. A piece placed at another
    /// drawable's bone (a wheel, in a group) often carries that drawable's skeleton: it is drawn once, by its owner.
    /// </summary>
    public static DrawableOverlays Build(IReadOnlyList<SceneModel> models, Bound? fragmentBound, Vector3 origin, float radius,
        IReadOnlyList<DrawableLight>? fragmentLights = null)
    {
        var drawn = new HashSet<Skeleton>(ReferenceEqualityComparer.Instance);
        var skeleton = new ViewportMesh(origin);
        var lights = new ViewportMesh(origin);
        var solid = new List<ViewportVertex>();
        float joint = Math.Max(radius, .1f) * .012f;
        foreach (SceneModel model in models)
        {
            Drawable drawable = model.Drawable;
            Matrix4x4[]? bones = drawable.Skeleton?.DeriveAbsoluteTransforms();
            if (drawable.Skeleton is { } rig && bones is not null && model.Group is null && drawn.Add(rig))
                for (int i = 0; i < rig.Bones.Count && i < bones.Length; i++)
                {
                    Vector3 at = (bones[i] * model.Transform).Translation;
                    Cross(skeleton, at, joint, BoneColour);
                    int parent = rig.Bones[i].ParentIndex;
                    if (parent >= 0 && parent < bones.Length)
                        skeleton.Line((bones[parent] * model.Transform).Translation, at, BoneColour, Width);
                }
            if (fragmentBound is null && drawable.Bound is { } bound)
                Collision(solid, bound, model.Transform, origin);
            foreach (DrawableLight light in drawable.Lights)
                Light(lights, light.Attributes, BoneOf(drawable, bones, light.Attributes.BoneTag) * model.Transform, joint);
        }
        if (fragmentBound is not null)
            Collision(solid, fragmentBound, Matrix4x4.Identity, origin);
        if (fragmentLights is { Count: > 0 } && models.Count > 0)
        {
            SceneModel main = models[0];
            Matrix4x4[]? bones = main.Drawable.Skeleton?.DeriveAbsoluteTransforms();
            foreach (DrawableLight light in fragmentLights)
                Light(lights, light.Attributes, BoneOf(main.Drawable, bones, light.Attributes.BoneTag) * main.Transform, joint);
        }
        return new(skeleton.SegmentArray(), lights.SegmentArray()) { Solid = [.. solid] };
    }

    /// <summary>Where the bone tagged <paramref name="tag"/> is, which a light moves with; the drawable's origin without one.</summary>
    private static Matrix4x4 BoneOf(Drawable drawable, Matrix4x4[]? bones, short tag) =>
        drawable.Skeleton?.FindBone((ushort)tag) is { } bone && bones is not null && bone.Index < bones.Length ? bones[bone.Index] : Matrix4x4.Identity;

    /// <summary>A collision's triangles, relative to the scene's origin.</summary>
    private static void Collision(List<ViewportVertex> solid, Bound bound, Matrix4x4 transform, Vector3 origin)
    {
        ViewportMesh triangles = CollisionMesh.Build(bound, transform);
        foreach (ViewportVertex corner in triangles.Vertices)
            solid.Add(corner with { Position = corner.Position + triangles.Origin - origin });
    }

    private static void Light(ViewportMesh into, LightAttrDef light, Matrix4x4 transform, float size)
    {
        uint colour = ViewportMesh.Pack(light.Color.R, light.Color.G, light.Color.B);
        Vector3 at = Vector3.Transform(light.Position, transform);
        Cross(into, at, size * 1.5f, colour);
        float reach = light.Falloff;
        if (!(reach > 0) || !float.IsFinite(reach))
            return;
        Vector3 direction = Normalized(Vector3.TransformNormal(light.Direction, transform), -Vector3.UnitZ);
        // Across the direction: the tangent, as square to it as can be.
        Vector3 tangent = Vector3.TransformNormal(light.Tangent, transform);
        Vector3 u = Normalized(tangent - direction * Vector3.Dot(tangent, direction), Math.Abs(direction.Z) < .9f ? Vector3.Normalize(Vector3.Cross(direction, Vector3.UnitZ)) : Vector3.UnitX);
        Vector3 v = Vector3.Cross(direction, u);
        if (light.LightType == SpotLight)
        {
            // The outer cone, out to where the light ends.
            float angle = Math.Clamp(light.ConeOuterAngle, 1, 89) * MathF.PI / 180;
            Vector3 middle = at + direction * (reach * MathF.Cos(angle));
            float spread = reach * MathF.Sin(angle);
            Circle(into, middle, u, v, spread, colour);
            foreach (Vector3 side in (ReadOnlySpan<Vector3>)[u, v, -u, -v])
                into.Line(at, middle + side * spread, colour, Width);
            return;
        }
        Circle(into, at, u, v, reach, colour);
        Circle(into, at, u, direction, reach, colour);
        Circle(into, at, v, direction, reach, colour);
    }

    private static Vector3 Normalized(Vector3 value, Vector3 fallback) =>
        value.LengthSquared() > 1e-10f && float.IsFinite(value.LengthSquared()) ? Vector3.Normalize(value) : fallback;

    private static void Cross(ViewportMesh into, Vector3 at, float size, uint colour)
    {
        into.Line(at - Vector3.UnitX * size, at + Vector3.UnitX * size, colour, Width);
        into.Line(at - Vector3.UnitY * size, at + Vector3.UnitY * size, colour, Width);
        into.Line(at - Vector3.UnitZ * size, at + Vector3.UnitZ * size, colour, Width);
    }

    private static void Circle(ViewportMesh into, Vector3 center, Vector3 u, Vector3 v, float radius, uint colour)
    {
        Vector3 previous = center + u * radius;
        for (int i = 1; i <= CircleSegments; i++)
        {
            float angle = i * MathF.Tau / CircleSegments;
            Vector3 next = center + (u * MathF.Cos(angle) + v * MathF.Sin(angle)) * radius;
            into.Line(previous, next, colour, Width);
            previous = next;
        }
    }
}
