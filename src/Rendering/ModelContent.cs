using System.Numerics;
using CommonFury.Numerics;
using FiveFury.Textures;
using FiveFury.Ybn;
using FiveFury.Ydr;
using FiveFury.Ynv;
using FiveFury.Ytd;

namespace HikariHopper.Rendering;

/// <summary>
/// A 3D file's content for the viewport: its triangles, and the box around them. Collisions and navmeshes are placed
/// at their map coordinates, so the viewer goes to them rather than to the world's origin; a drawable is shown around
/// its own origin, with its materials, as <see cref="Surfaces"/>.
/// </summary>
internal sealed record ModelContent(Aabb3 Box, ViewportMesh Mesh, DrawableScene? Surfaces = null)
{
    /// <summary>
    /// Reads a collision (.ybn), a navmesh (.ynv) or a drawable (.ydr). A drawable's textures come from its own
    /// dictionary, then from <paramref name="dictionaries"/> (.ytd files found beside it), the first holding a name
    /// winning; ones that can't be read are left out.
    /// </summary>
    /// <exception cref="InvalidDataException">The file isn't one this can read.</exception>
    public static ModelContent Read(string name, ReadOnlySpan<byte> data, IReadOnlyList<byte[]>? dictionaries = null)
    {
        if (name.EndsWith(".ydr", StringComparison.OrdinalIgnoreCase))
        {
            Ydr ydr = Ydr.Read(data);
            var external = new List<TextureDictionary>();
            foreach (byte[] dictionary in dictionaries ?? [])
            {
                try
                {
                    external.Add(Ytd.Read(dictionary).Dictionary);
                }
                catch (Exception error) when (error is InvalidDataException or EndOfStreamException or ArgumentException or NotSupportedException)
                {
                }
            }
            DrawableScene scene = DrawableScene.Build(ydr.Drawable, external);
            return new ModelContent(scene.Box, new ViewportMesh(scene.Origin), scene);
        }
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
        throw new InvalidDataException($"{name} isn't a collision, navmesh or drawable file.");
    }
}
