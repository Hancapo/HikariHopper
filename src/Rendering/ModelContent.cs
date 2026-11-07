using System.Numerics;
using CommonFury.Numerics;
using FiveFury.Ybn;
using FiveFury.Ynv;

namespace HikariHopper.Rendering;

/// <summary>
/// A 3D file's content for the viewport: its triangles, and the box around them in the world. Collisions and navmeshes
/// are placed at their map coordinates, so the viewer goes to them rather than to the world's origin.
/// </summary>
internal sealed record ModelContent(Aabb3 Box, ViewportMesh Mesh)
{
    /// <summary>Reads a collision (.ybn) or a navmesh (.ynv).</summary>
    /// <exception cref="InvalidDataException">The file isn't a collision or navmesh this can read.</exception>
    public static ModelContent Read(string name, ReadOnlySpan<byte> data)
    {
        if (name.EndsWith(".ybn", StringComparison.OrdinalIgnoreCase))
        {
            Ybn ybn = Ybn.Read(data);
            return new ModelContent(ybn.Bound.Box, CollisionMesh.Build(ybn));
        }
        if (name.EndsWith(".ynv", StringComparison.OrdinalIgnoreCase))
        {
            // Navmesh vertices are decoded to world coordinates.
            Ynv navmesh = Ynv.Read(data);
            List<Vector3> vertices = navmesh.Vertices;
            if (vertices.Count == 0)
                return new ModelContent(Aabb3.Empty, new ViewportMesh(Vector3.Zero));
            Vector3 minimum = vertices[0], maximum = vertices[0];
            foreach (Vector3 vertex in vertices)
            {
                minimum = Vector3.Min(minimum, vertex);
                maximum = Vector3.Max(maximum, vertex);
            }
            var box = new Aabb3(minimum, maximum);
            return new ModelContent(box, NavmeshMesh.Build(navmesh, box.Center));
        }
        throw new InvalidDataException($"{name} isn't a collision or navmesh file.");
    }
}
