using System.Numerics;
using FiveFury.Ynv;
using T = HikariHopper.Ui.Theme;

namespace HikariHopper.Rendering;

/// <summary>A navmesh's polygons as triangles, each fanned from its first corner, in the app's accent blue.</summary>
internal static class NavmeshMesh
{
    /// <summary>The navmesh's triangles, relative to the middle of its vertices.</summary>
    public static ViewportMesh Build(Ynv navmesh, Vector3 origin)
    {
        var mesh = new ViewportMesh(origin);
        Avalonia.Media.Color accent = T.Accent.Color;
        uint colour = ViewportMesh.Pack(accent.R, accent.G, accent.B);
        List<Vector3> vertices = navmesh.Vertices;
        List<ushort> indices = navmesh.Indices;
        foreach (YnvPoly polygon in navmesh.Polygons)
        {
            int first = polygon.IndexId, count = polygon.IndexCount;
            if (count < 3 || first + count > indices.Count)
            {
                mesh.Skip("polygon with corners out of range");
                continue;
            }
            Vector3 Corner(int i) => vertices[indices[first + i]];
            bool inRange = true;
            for (int i = 0; i < count; i++)
                inRange &= indices[first + i] < vertices.Count;
            if (!inRange)
            {
                mesh.Skip("polygon with corners out of range");
                continue;
            }
            for (int i = 1; i + 1 < count; i++)
                mesh.Triangle(Corner(0), Corner(i), Corner(i + 1), colour);
        }
        return mesh;
    }
}
