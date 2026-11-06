using System.Numerics;
using FiveFury.Ynv;
using T = HikariHopper.Ui.Theme;

namespace HikariHopper.Rendering;

/// <summary>
/// A navmesh as the viewport draws it: its polygons, each fanned into triangles and coloured by what kind of ground it
/// is, outlined as the polygon it is; the edges peds climb, drop or take cover at; its portals (ladders and climbable
/// objects); and its cover points, each facing the way it gives cover.
/// </summary>
internal static class NavmeshMesh
{
    // Lines sit this far above the ground they mark, so the polygons don't hide them.
    private const float Lift = 0.06f;
    private const float OutlineWidth = 1.25f;
    private const float EdgeWidth = 3;
    private const float CoverEdgeWidth = 2;
    private const float LinkWidth = 3;
    private const float PostHeight = 1.2f;
    private const float CoverHeight = 0.8f;
    private const float CoverReach = 0.6f;

    private static readonly uint Outline = Pack("#1B1C2C");

    /// <summary>The ground kinds, most telling first: a polygon takes the first that applies.</summary>
    private static readonly (string Name, uint Colour, Func<YnvPoly, bool> Applies)[] Grounds =
    [
        ("Too steep", ViewportMesh.Pack(T.Error), poly => poly.Flags0.HasFlag(YnvPolyFlags0.TooSteepToWalkOn)),
        ("Water", ViewportMesh.Pack(T.Accent), poly => poly.Flags0.HasFlag(YnvPolyFlags0.IsWater)),
        ("Shallow water", Pack("#9DB9E6"), poly => poly.Flags1.HasFlag(YnvPolyFlags1.IsShallowWater)),
        ("Train track", ViewportMesh.Pack(T.Brass), poly => poly.Flags1.HasFlag(YnvPolyFlags1.IsTrainTrack)),
        ("Road", Pack("#7E809B"), poly => poly.Flags1.HasFlag(YnvPolyFlags1.IsRoad)),
        ("Interior", ViewportMesh.Pack(T.InkData), poly => poly.Flags1.HasFlag(YnvPolyFlags1.IsInterior)),
        ("Pavement", ViewportMesh.Pack(T.TextRow), poly => poly.Flags0.HasFlag(YnvPolyFlags0.IsPavement)),
        ("Sheltered", ViewportMesh.Pack(T.InkAsset), poly => poly.Flags0.HasFlag(YnvPolyFlags0.InShelter)),
        ("Walkable", ViewportMesh.Pack(T.Ok), _ => true),
    ];

    // Lines in hues the ground colours don't use, each kind its own: what peds do at an edge or a link should read
    // apart at a glance. Cover edges are the commonest, so they are the thinnest and the calmest.
    private static readonly uint ClimbLow = Pack("#FF9F43");
    private static readonly uint ClimbHigh = Pack("#FF5E3A");
    private static readonly uint Drop = Pack("#C792EA");
    private static readonly uint HighDrop = Pack("#FF5FA2");
    private static readonly uint CoverEdge = ViewportMesh.Pack(T.Brass);
    private static readonly uint Ladder = ViewportMesh.Pack(T.Selection);
    private static readonly uint ClimbObject = Pack("#7FD1C7");
    private static readonly uint LowCover = Pack("#9AD0F5");
    private static readonly uint WallCover = ViewportMesh.Pack(T.Text);
    private static readonly uint DisabledCover = ViewportMesh.Pack(T.TextFaint);

    /// <summary>The navmesh, relative to the middle of its vertices.</summary>
    public static ViewportMesh Build(Ynv navmesh, Vector3 origin)
    {
        var mesh = new ViewportMesh(origin);
        AddPolygons(navmesh, mesh);
        AddPortals(navmesh, mesh);
        AddCoverPoints(navmesh, mesh);
        return mesh;
    }

    /// <summary>A polygon's ground kind, as the legend names it.</summary>
    internal static string Ground(YnvPoly polygon) => Grounds.First(ground => ground.Applies(polygon)).Name;

    private static void AddPolygons(Ynv navmesh, ViewportMesh mesh)
    {
        List<Vector3> vertices = navmesh.Vertices;
        List<ushort> indices = navmesh.Indices;
        List<YnvEdge> edges = navmesh.Edges;
        var corners = new List<Vector3>();
        foreach (YnvPoly polygon in navmesh.Polygons)
        {
            int first = polygon.IndexId, count = polygon.IndexCount;
            corners.Clear();
            if (count >= 3 && first + count <= indices.Count)
                for (int i = 0; i < count && indices[first + i] < vertices.Count; i++)
                    corners.Add(vertices[indices[first + i]]);
            if (corners.Count != count || count < 3)
            {
                mesh.Skip("polygon with corners out of range");
                continue;
            }
            (string name, uint colour, _) = Grounds.First(ground => ground.Applies(polygon));
            mesh.Explain(name, colour);
            // Fanned into triangles whose own edges aren't drawn: they aren't the polygon's.
            for (int i = 1; i + 1 < count; i++)
                mesh.Triangle(corners[0], corners[i], corners[i + 1], colour, edges: false);
            // Each corner's edge runs to the next corner; the navmesh keeps one edge record per corner.
            for (int i = 0; i < count; i++)
            {
                Vector3 a = corners[i] + Vector3.UnitZ * Lift, b = corners[(i + 1) % count] + Vector3.UnitZ * Lift;
                if (first + i < edges.Count && Special(edges[first + i]) is { } special)
                {
                    mesh.Line(a, b, special.Colour, special.Colour == CoverEdge ? CoverEdgeWidth : EdgeWidth);
                    mesh.Explain(special.Name, special.Colour);
                }
                else
                    mesh.Line(a, b, Outline, OutlineWidth);
            }
        }
    }

    /// <summary>What an edge is for, when it is more than a way to the next polygon.</summary>
    private static (string Name, uint Colour)? Special(YnvEdge edge)
    {
        switch (edge.Current.AdjacencyType)
        {
            case YnvAdjacencyType.ClimbLow:
                return ("Low climb", ClimbLow);
            case YnvAdjacencyType.ClimbHigh:
                return ("High climb", ClimbHigh);
            case YnvAdjacencyType.Dropdown:
                return edge.Flags.HasFlag(YnvEdgeFlags.HighDropOverEdge) ? ("High drop", HighDrop) : ("Drop", Drop);
        }
        if (edge.Flags.HasFlag(YnvEdgeFlags.HighDropOverEdge))
            return ("High drop", HighDrop);
        if (edge.Flags.HasFlag(YnvEdgeFlags.EdgeProvidesCover))
            return ("Cover edge", CoverEdge);
        return null;
    }

    // A link from one place to another, with a post at each end so its ends show from any angle.
    private static void AddPortals(Ynv navmesh, ViewportMesh mesh)
    {
        foreach (YnvPortal portal in navmesh.Portals)
        {
            // An end in a neighbouring navmesh has no position until that mesh is loaded.
            if (!portal.IsFromPositionResolved || !portal.IsToPositionResolved)
            {
                mesh.Skip("portal into another navmesh");
                continue;
            }
            (string name, uint colour) = portal.Type switch
            {
                YnvPortalType.ClimbLadder => ("Ladder up", Ladder),
                YnvPortalType.DescendLadder => ("Ladder down", Ladder),
                _ => ("Climb over", ClimbObject),
            };
            mesh.Explain(name, colour);
            Vector3 from = portal.PositionFrom + Vector3.UnitZ * Lift, to = portal.PositionTo + Vector3.UnitZ * Lift;
            mesh.Line(from, to, colour, LinkWidth);
            mesh.Line(from, from + Vector3.UnitZ * PostHeight, colour, LinkWidth);
            mesh.Line(to, to + Vector3.UnitZ * PostHeight, colour, LinkWidth);
        }
    }

    // A post where a ped can take cover, and a tick along the ground the way it faces.
    private static void AddCoverPoints(Ynv navmesh, ViewportMesh mesh)
    {
        foreach (YnvPoint point in navmesh.Points)
        {
            (string name, uint colour) = point.Disabled ? ("Cover point, disabled", DisabledCover)
                : point.Type is YnvPointType.LowWall or YnvPointType.LowWallToLeft or YnvPointType.LowWallToRight ? ("Cover point, low", LowCover)
                : ("Cover point, wall", WallCover);
            mesh.Explain(name, colour);
            Vector3 foot = point.Position + Vector3.UnitZ * Lift;
            // The heading turns from north (+Y) towards west, as the game's headings do.
            var facing = new Vector3(-MathF.Sin(point.Direction), MathF.Cos(point.Direction), 0);
            mesh.Line(foot, foot + Vector3.UnitZ * CoverHeight, colour, LinkWidth);
            mesh.Line(foot, foot + facing * CoverReach, colour, LinkWidth);
        }
    }

    private static uint Pack(string hex)
    {
        var colour = Avalonia.Media.Color.Parse(hex);
        return ViewportMesh.Pack(colour.R, colour.G, colour.B);
    }
}
