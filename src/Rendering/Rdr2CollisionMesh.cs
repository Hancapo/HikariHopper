using System.Numerics;
using CommonFury.Collisions;
using RedFury.Collisions;

namespace HikariHopper.Rendering;

/// <summary>
/// Red Dead Redemption 2's collisions and navmeshes as the viewer's triangles, after RedFury's CollisionMesh: each bound
/// where its composite places it, each polygon of its geometry, its spheres, capsules, boxes, cylinders and planes,
/// marked and coloured by surface material. RDR2's materials carry no display colour: the hue follows the material's
/// filter group in the game's materials.meta, the lightness varies by material, as RedFury colours them.
/// </summary>
internal sealed class Rdr2CollisionMesh
{
    private const int Segments = 16, HemisphereRings = 6;
    // Infinite planes are drawn as squares of this half-size around their stored position.
    private const float PlaneHalfSize = 10;

    private readonly ViewportMesh mesh;
    private readonly CollisionMaterialCatalog? catalog;
    private readonly Dictionary<ushort, (string Name, uint Colour)> materials = [];
    private Matrix4x4 transform;
    private uint colour;

    private Rdr2CollisionMesh(ViewportMesh mesh, CollisionMaterialCatalog? catalog) => (this.mesh, this.catalog) = (mesh, catalog);

    /// <summary>A collision's triangles, relative to the middle of its box; materials named from <paramref name="catalog"/>.</summary>
    public static ViewportMesh Build(RedFury.Ybn.Ybn ybn, CollisionMaterialCatalog? catalog)
    {
        Bound root = ybn.Root;
        var box = new CommonFury.Numerics.Aabb3(root.BoxMin, root.BoxMax);
        var builder = new Rdr2CollisionMesh(new ViewportMesh(box.IsEmpty ? Vector3.Zero : box.Center), catalog);
        foreach (BoundInstance instance in root.Instances())
            builder.Add(instance);
        builder.mesh.Mark(-1);
        return builder.mesh;
    }

    /// <summary>
    /// A navmesh's polygons, at their world coordinates, relative to the middle of their box: fanned into triangles, as
    /// RedFury's viewer draws them.
    /// </summary>
    public static (ViewportMesh Mesh, CommonFury.Numerics.Aabb3 Box) Navmesh(RedFury.Ynv.Ynv navmesh)
    {
        Vector3[] points = [.. navmesh.Vertices.ToArray().Select(v => new Vector3((float)v.X, (float)v.Y, (float)v.Z))];
        if (points.Length == 0)
            return (new ViewportMesh(Vector3.Zero), CommonFury.Numerics.Aabb3.Empty);
        var box = new CommonFury.Numerics.Aabb3(points.Aggregate(Vector3.Min), points.Aggregate(Vector3.Max));
        var mesh = new ViewportMesh(box.Center);
        uint ground = ViewportMesh.Pack(115, 191, 153);
        int[] fan = navmesh.TriangleFanIndices();
        for (int i = 0; i + 2 < fan.Length; i += 3)
            if ((uint)fan[i] < points.Length && (uint)fan[i + 1] < points.Length && (uint)fan[i + 2] < points.Length)
            {
                mesh.Mark(mesh.Explain("Navigable ground", ground, NavmeshMesh.GroundSection));
                mesh.Triangle(points[fan[i]], points[fan[i + 1]], points[fan[i + 2]], ground);
            }
        mesh.Mark(-1);
        return (mesh, box);
    }

    private void Add(BoundInstance instance)
    {
        transform = instance.Transform;
        Bound bound = instance.Bound;
        if (bound is GeometryBound geometry)
        {
            Add(geometry);
            return;
        }
        Select((ushort)bound.MaterialWord1);
        switch (bound.Kind)
        {
            case BoundKind.Sphere:
                Capsule(bound.BoxCenter, bound.BoxCenter, bound.SphereRadius);
                break;
            // Native endpoints use the centroid, local Y and half-length radius - margin; the margin is the radius.
            case BoundKind.Capsule:
                Vector3 half = Vector3.UnitY * Math.Max(bound.SphereRadius - bound.Margin, 0);
                Capsule(bound.BoxCenter - half, bound.BoxCenter + half, bound.Margin);
                break;
            case BoundKind.Box:
                Box(bound.BoxMin, new(bound.BoxMax.X, bound.BoxMax.Y, bound.BoxMin.Z), new(bound.BoxMax.X, bound.BoxMin.Y, bound.BoxMax.Z),
                    new(bound.BoxMin.X, bound.BoxMax.Y, bound.BoxMax.Z));
                break;
            case BoundKind.Cylinder when bound is CylinderBound cylinder:
                Vector3 middle = (bound.BoxMin + bound.BoxMax) * .5f;
                Cylinder(middle - Vector3.UnitY * cylinder.HalfHeight, middle + Vector3.UnitY * cylinder.HalfHeight, cylinder.Radius);
                break;
            case BoundKind.Plane when bound is PlaneBound plane:
                Plane(plane.Position, plane.Normal);
                break;
            case BoundKind.Composite:
                break;
            default:
                mesh.Skip($"bound kind {(byte)bound.Kind}");
                break;
        }
    }

    private void Add(GeometryBound geometry)
    {
        Vector3[] vertices = geometry.Vertices.ToArray();
        foreach (var polygon in geometry.Polygons)
        {
            Select(polygon.MaterialIndex < geometry.Materials.Count ? geometry.Materials[polygon.MaterialIndex].MaterialId : (ushort)0);
            bool inRange = true;
            for (int i = 0; i < polygon.VertexCount; i++)
                inRange &= (uint)polygon.VertexIndex(i) < (uint)vertices.Length;
            if (!inRange)
            {
                mesh.Skip("polygon with an out-of-range vertex");
                continue;
            }
            Vector3 Point(int i) => vertices[polygon.VertexIndex(i)];
            switch (polygon.Kind)
            {
                case BoundPolygonKind.Triangle: Triangle(Point(0), Point(1), Point(2)); break;
                case BoundPolygonKind.Sphere: Capsule(Point(0), Point(0), polygon.Radius!.Value); break;
                case BoundPolygonKind.Capsule: Capsule(Point(0), Point(1), polygon.Radius!.Value); break;
                case BoundPolygonKind.Cylinder: Cylinder(Point(0), Point(1), polygon.Radius!.Value); break;
                case BoundPolygonKind.Box: Box(Point(0), Point(1), Point(2), Point(3)); break;
                default: mesh.Skip($"polygon type {(int)polygon.Kind}"); break;
            }
        }
    }

    // What is drawn next wears the material's mark, named and coloured as RedFury does, and counts as one more of its
    // shapes in the legend.
    private void Select(ushort id)
    {
        if (!materials.TryGetValue(id, out var known))
        {
            CollisionMaterialDefinition? definition = catalog?.Find(id);
            string name = definition?.Name.ToLowerInvariant().Replace('_', ' ') ?? (id == 0 ? "material 0" : $"material 0x{id:X4}");
            name = char.ToUpperInvariant(name[0]) + name[1..];
            Vector4 rgb = Colour(id, definition) * 255;
            uint packed = ViewportMesh.Pack((byte)rgb.X, (byte)rgb.Y, (byte)rgb.Z);
            materials[id] = known = (name, packed);
        }
        mesh.Mark(mesh.Explain(known.Name, known.Colour, CollisionMesh.Section));
        colour = known.Colour;
    }

    /// <summary>RedFury's colour for a material: its filter group's hue, its own lightness; a stable hue for one unlisted.</summary>
    internal static Vector4 Colour(ushort id, CollisionMaterialDefinition? definition)
    {
        uint hash = RedFury.Hashing.Jenkins.Hash(definition?.Name ?? id.ToString("X4"));
        Vector3? group = definition?.Filter switch
        {
            "HARD_TERRAIN" => new(.56f, .56f, .6f),
            "LOOSE_TERRAIN" => new(.74f, .6f, .4f),
            "DEFORMABLE_TERRAIN" => new(.52f, .4f, .28f),
            "DYNAMIC_POM_TERRAIN" => new(.66f, .62f, .44f),
            "VEGETATION" => new(.36f, .64f, .3f),
            "WOODS" => new(.62f, .42f, .24f),
            "METALS" => new(.5f, .6f, .72f),
            "MANMADE" => new(.8f, .54f, .42f),
            "GLASS" => new(.55f, .86f, .92f),
            "LIQUIDS" => new(.22f, .46f, .88f),
            "NATURAL_MOTION" => new(.88f, .58f, .64f),
            "VEHICLES" => new(.62f, .48f, .78f),
            "SPECIAL" => new(.95f, .78f, .22f),
            "MISC" or "DEFAULT" => new(.7f, .7f, .66f),
            "UNUSED" => new(.86f, .26f, .26f),
            _ => null,
        };
        if (group is { } rgb)
        {
            float luma = Vector3.Dot(rgb, new(.2126f, .7152f, .0722f));
            return new(Vector3.Clamp(Vector3.Lerp(new(luma), rgb, 1.7f) * (.8f + (hash & 0xFF) / 255f * .4f), Vector3.Zero, Vector3.One), 1);
        }
        float hue = (hash & 0xFFFF) / 65536f * 6;
        Vector3 pure = new(Math.Clamp(Math.Abs(hue - 3) - 1, 0, 1), Math.Clamp(2 - Math.Abs(hue - 2), 0, 1), Math.Clamp(2 - Math.Abs(hue - 4), 0, 1));
        return new(Vector3.Lerp(new(.7f), pure, .7f), 1);
    }

    private Vector3 At(Vector3 position) => Vector3.Transform(position, transform);

    // Flat triangles and quads, each face its own, with their edges.
    private void Triangle(Vector3 a, Vector3 b, Vector3 c) => mesh.Triangle(At(a), At(b), At(c), colour);

    private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        Triangle(a, b, c);
        Triangle(a, c, d);
    }

    // Four alternating cuboid corners; the other four are their reflections through the centroid.
    private void Box(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        Vector3[] corners = [a, b, c, d];
        Vector3 twice = (a + b + c + d) * .5f;
        for (int i = 0; i < 4; i++)
            for (int j = i + 1; j < 4; j++)
            {
                int[] others = [.. Enumerable.Range(0, 4).Where(k => k != i && k != j)];
                Quad(corners[i], twice - corners[others[0]], corners[j], twice - corners[others[1]]);
            }
    }

    // A hemisphere at each end joined by the body; equal ends make a sphere.
    private void Capsule(Vector3 a, Vector3 b, float radius)
    {
        Vector3 axis = b - a, up = axis.LengthSquared() > 1e-12f ? Vector3.Normalize(axis) : Vector3.UnitZ;
        Basis(up, out Vector3 u, out Vector3 v);
        const int rings = 2 * HemisphereRings + 2;
        var points = new Vector3[rings * Segments];
        for (int k = 0; k < rings; k++)
        {
            float angle = (k <= HemisphereRings ? k : k - 1) * MathF.PI / (2 * HemisphereRings);
            Vector3 center = k <= HemisphereRings ? b : a;
            for (int s = 0; s < Segments; s++)
            {
                float t = s * MathF.Tau / Segments;
                points[k * Segments + s] = center + up * (MathF.Cos(angle) * radius) + (u * MathF.Cos(t) + v * MathF.Sin(t)) * (MathF.Sin(angle) * radius);
            }
        }
        Strip(points, rings);
    }

    private void Cylinder(Vector3 a, Vector3 b, float radius)
    {
        Vector3 axis = b - a;
        if (axis.LengthSquared() <= 1e-12f)
        {
            mesh.Skip("degenerate cylinder");
            return;
        }
        Vector3 up = Vector3.Normalize(axis);
        Basis(up, out Vector3 u, out Vector3 v);
        Vector3 Rim(Vector3 center, int s)
        {
            float t = s * MathF.Tau / Segments;
            return center + (u * MathF.Cos(t) + v * MathF.Sin(t)) * radius;
        }
        var points = new Vector3[2 * Segments];
        for (int s = 0; s < Segments; s++)
        {
            points[s] = Rim(b, s);
            points[Segments + s] = Rim(a, s);
        }
        Strip(points, 2);
        foreach (Vector3 center in new[] { a, b })
            for (int s = 0; s < Segments; s++)
                Triangle(center, Rim(center, s), Rim(center, (s + 1) % Segments));
    }

    private void Plane(Vector3 position, Vector3 normal)
    {
        if (normal.LengthSquared() <= 1e-12f)
        {
            mesh.Skip("degenerate plane");
            return;
        }
        Basis(Vector3.Normalize(normal), out Vector3 u, out Vector3 v);
        u *= PlaneHalfSize;
        v *= PlaneHalfSize;
        Quad(position - u - v, position + u - v, position + u + v, position - u + v);
        mesh.Skip("infinite plane drawn as a 20 m square");
    }

    private void Strip(Vector3[] points, int rings)
    {
        for (int r = 0; r + 1 < rings; r++)
            for (int s = 0; s < Segments; s++)
            {
                int i0 = r * Segments + s, i1 = r * Segments + (s + 1) % Segments;
                Triangle(points[i0], points[i0 + Segments], points[i1 + Segments]);
                Triangle(points[i0], points[i1 + Segments], points[i1]);
            }
    }

    private static void Basis(Vector3 axis, out Vector3 u, out Vector3 v)
    {
        u = Vector3.Normalize(Vector3.Cross(axis, Math.Abs(axis.Z) < .9f ? Vector3.UnitZ : Vector3.UnitX));
        v = Vector3.Cross(axis, u);
    }
}
