using System.Numerics;
using FiveFury.Bounds;
using FiveFury.Ybn;

namespace HikariHopper.Rendering;

/// <summary>
/// A collision's shapes as triangles (after RedFury's CollisionMesh): its composite's children where their
/// transforms put them, each polygon of their geometry, and their spheres, capsules, boxes and cylinders, coloured
/// by surface material.
/// </summary>
internal sealed class CollisionMesh
{
    private const int Segments = 16;
    private const int HemisphereRings = 6;
    private static readonly uint Fallback = ViewportMesh.Pack(145, 149, 98);

    private readonly ViewportMesh mesh;
    private Matrix4x4 transform;
    private uint colour;
    // The current material's legend name; null when it has none.
    private string? material;

    private CollisionMesh(ViewportMesh mesh) => this.mesh = mesh;

    /// <summary>The collision's triangles, relative to the middle of its box.</summary>
    public static ViewportMesh Build(Ybn ybn)
    {
        var builder = new CollisionMesh(new ViewportMesh(ybn.Bound.Box.Center));
        builder.Add(ybn.Bound, Matrix4x4.Identity);
        return builder.mesh;
    }

    /// <summary>A bound's triangles where <paramref name="transform"/> puts it, at their own positions (an origin of zero).</summary>
    public static ViewportMesh Build(Bound bound, Matrix4x4 transform)
    {
        var builder = new CollisionMesh(new ViewportMesh(Vector3.Zero));
        builder.Add(bound, transform);
        return builder.mesh;
    }

    /// <summary>The material's display colour from FiveFury's catalogue, or the default's.</summary>
    internal static uint Colour(BoundMaterialType type) =>
        BoundMaterialLibrary.Default.Entries.TryGetValue(type, out BoundMaterialInfo info) && info.DisplayColor is { } rgb
            ? ViewportMesh.Pack(rgb.R, rgb.G, rgb.B)
            : Fallback;

    /// <summary>The material's name as the legend shows it: "Tarmac painted" for TARMAC_PAINTED.</summary>
    internal static string Name(BoundMaterialType type)
    {
        string name = BoundMaterialLibrary.Default.Entries.TryGetValue(type, out BoundMaterialInfo info) ? info.Name : $"Material {(byte)type}";
        name = name.Replace('_', ' ').ToLowerInvariant();
        return name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];
    }

    /// <summary>The heading the parts panel lists a collision's materials under.</summary>
    internal const string Section = "Materials";

    // What is drawn next wears the material's mark, which the parts panel lists.
    private void Material(BoundMaterialType type)
    {
        colour = Colour(type);
        material = Name(type);
        mesh.Mark(material, colour, Section);
    }

    /// <summary>Counts the material for the legend if the shape just added drew anything: skipped ones don't count.</summary>
    private void Counted(int trianglesBefore)
    {
        if (material is not null && mesh.TriangleCount > trianglesBefore)
            mesh.Explain(material, colour, Section);
    }

    private void Add(Bound bound, Matrix4x4 parent)
    {
        switch (bound)
        {
            case BoundComposite composite:
                foreach (BoundChild child in composite.Children)
                    if (child.Bound is { } inner)
                        Add(inner, (child.Transform?.Matrix ?? Matrix4x4.Identity) * parent);
                return;
            case BoundGeometry geometry:
                transform = parent;
                Add(geometry);
                return;
        }
        transform = parent;
        Material(bound.Material.Type);
        Vector3 center = bound.Box.Center;
        int before = mesh.TriangleCount;
        switch (bound)
        {
            case BoundSphere:
                Capsule(center, center, bound.SphereRadius);
                break;
            case BoundBox:
                Vector3 min = bound.Box.Minimum, max = bound.Box.Maximum;
                Box(min, new(max.X, max.Y, min.Z), new(max.X, min.Y, max.Z), new(min.X, max.Y, max.Z));
                break;
            // The shaft runs along local Y; the margin is the radius.
            case BoundCapsule capsule:
                Vector3 half = Vector3.UnitY * Math.Max(capsule.SphereRadius - capsule.Margin, 0);
                Capsule(center - half, center + half, capsule.Radius);
                break;
            default:
                mesh.Skip(bound.Type.ToString());
                break;
        }
        Counted(before);
    }

    private void Add(BoundGeometry geometry)
    {
        List<Vector3> vertices = geometry.Vertices;
        foreach (BoundPolygon polygon in geometry.Polygons)
        {
            if (polygon.MaterialIndex < geometry.Materials.Count)
                Material(geometry.Materials[polygon.MaterialIndex].Type);
            else
            {
                (colour, material) = (Fallback, null);
                mesh.Mark(-1);
            }
            bool inRange = true;
            for (int i = 0; i < polygon.VertexCount; i++)
                inRange &= polygon.VertexIndex(i) < vertices.Count;
            if (!inRange)
            {
                mesh.Skip("polygon with a vertex out of range");
                continue;
            }
            Vector3 Point(int slot) => vertices[polygon.VertexIndex(slot)];
            int before = mesh.TriangleCount;
            switch (polygon)
            {
                case BoundTriangle:
                    Triangle(Point(0), Point(1), Point(2));
                    break;
                case BoundPolygonSphere sphere:
                    Capsule(Point(0), Point(0), sphere.Radius);
                    break;
                case BoundPolygonCapsule capsule:
                    Capsule(Point(0), Point(1), capsule.Radius);
                    break;
                case BoundPolygonCylinder cylinder:
                    Cylinder(Point(0), Point(1), cylinder.Radius);
                    break;
                case BoundPolygonBox:
                    Box(Point(0), Point(1), Point(2), Point(3));
                    break;
                default:
                    mesh.Skip(polygon.Type.ToString());
                    break;
            }
            Counted(before);
        }
    }

    private void Triangle(Vector3 a, Vector3 b, Vector3 c) =>
        mesh.Triangle(Vector3.Transform(a, transform), Vector3.Transform(b, transform), Vector3.Transform(c, transform), colour);

    private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        Triangle(a, b, c);
        Triangle(a, c, d);
    }

    // Four alternate corners of a cuboid; the other four are their reflections through its middle.
    private void Box(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        Vector3[] corners = [a, b, c, d];
        Vector3 twice = (a + b + c + d) * .5f;
        for (int i = 0; i < 4; i++)
        {
            for (int j = i + 1; j < 4; j++)
            {
                int[] others = [.. Enumerable.Range(0, 4).Where(k => k != i && k != j)];
                Quad(corners[i], twice - corners[others[0]], corners[j], twice - corners[others[1]]);
            }
        }
    }

    // A hemisphere at each end joined by a cylinder; equal ends make a sphere.
    private void Capsule(Vector3 a, Vector3 b, float radius)
    {
        Vector3 axis = b - a, up = axis.LengthSquared() > 1e-12f ? Vector3.Normalize(axis) : Vector3.UnitZ;
        Basis(up, out Vector3 u, out Vector3 v);
        const int rings = 2 * HemisphereRings + 2;
        var points = new Vector3[rings, Segments];
        for (int k = 0; k < rings; k++)
        {
            float angle = (k <= HemisphereRings ? k : k - 1) * MathF.PI / (2 * HemisphereRings);
            Vector3 center = k <= HemisphereRings ? b : a;
            for (int s = 0; s < Segments; s++)
            {
                float t = s * MathF.Tau / Segments;
                points[k, s] = center + up * (MathF.Cos(angle) * radius) + (u * MathF.Cos(t) + v * MathF.Sin(t)) * (MathF.Sin(angle) * radius);
            }
        }
        Strip(points, rings);
    }

    private void Cylinder(Vector3 a, Vector3 b, float radius)
    {
        Vector3 axis = b - a;
        if (axis.LengthSquared() <= 1e-12f)
        {
            mesh.Skip("cylinder with no length");
            return;
        }
        Vector3 up = Vector3.Normalize(axis);
        Basis(up, out Vector3 u, out Vector3 v);
        Vector3 Rim(Vector3 center, int s)
        {
            float t = s * MathF.Tau / Segments;
            return center + (u * MathF.Cos(t) + v * MathF.Sin(t)) * radius;
        }
        var points = new Vector3[2, Segments];
        for (int s = 0; s < Segments; s++)
        {
            points[0, s] = Rim(b, s);
            points[1, s] = Rim(a, s);
        }
        Strip(points, 2);
        for (int s = 0; s < Segments; s++)
        {
            Triangle(a, Rim(a, s), Rim(a, (s + 1) % Segments));
            Triangle(b, Rim(b, (s + 1) % Segments), Rim(b, s));
        }
    }

    private void Strip(Vector3[,] points, int rings)
    {
        for (int r = 0; r + 1 < rings; r++)
        {
            for (int s = 0; s < Segments; s++)
            {
                int next = (s + 1) % Segments;
                Triangle(points[r, s], points[r + 1, s], points[r + 1, next]);
                Triangle(points[r, s], points[r + 1, next], points[r, next]);
            }
        }
    }

    private static void Basis(Vector3 axis, out Vector3 u, out Vector3 v)
    {
        u = Vector3.Normalize(Vector3.Cross(axis, Math.Abs(axis.Z) < .9f ? Vector3.UnitZ : Vector3.UnitX));
        v = Vector3.Cross(axis, u);
    }
}
