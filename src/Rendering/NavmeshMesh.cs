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
    // Bands along the edges peds climb, drop or take cover at, in metres; and how far above their polygon they lie.
    private const float EdgeRibbon = .16f;
    private const float CoverRibbon = .1f;
    private const float RibbonLift = .02f;
    private const float ArcMinimumRise = .35f;
    private const float ArcMaximumRise = 3;
    private const float PuckHeight = .05f;
    // A cover point's puck, and its panel: in front of it, half a ped's height for low cover, a ped's height for a wall.
    private const float CoverPuck = .14f;
    private const float CoverReach = .32f;
    private const float PanelWidth = .5f;
    private const float PanelThickness = .06f;
    private const float LowCoverHeight = .55f;
    private const float WallCoverHeight = 1.1f;

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
    // White: no ground colour is near it (train tracks are brass), so a cover edge shows on any of them.
    private static readonly uint CoverEdge = Pack("#F4F5FA");
    // Portals are solid shapes: the link in a saturated colour nothing else uses, so it stands out from the ground,
    // and its ends on dark pucks, which read as the bases it stands on rather than as more of the link.
    private static readonly uint Ladder = Pack("#FF4FD8");
    private static readonly uint ClimbObject = Pack("#22D3EE");
    private static readonly uint PortalBase = ViewportMesh.Pack(T.ChromeBg);
    // Two portal ends closer than this are the same place.
    private const float SamePlace = .05f;
    private static readonly uint LowCover = Pack("#38BDF8");
    private static readonly uint WallCover = Pack("#FACC15");
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
            // Each corner's edge runs to the next corner; the navmesh keeps one edge record per corner. Every edge has
            // the outline; one peds climb, drop or take cover at also gets a ribbon along it, lying on the polygon.
            Vector3 centroid = Vector3.Zero;
            foreach (Vector3 corner in corners)
                centroid += corner / count;
            for (int i = 0; i < count; i++)
            {
                Vector3 a = corners[i], b = corners[(i + 1) % count];
                if (first + i < edges.Count && Special(edges[first + i]) is { } special)
                {
                    Ribbon(mesh, a, b, centroid, special.Colour == CoverEdge ? CoverRibbon : EdgeRibbon, special.Colour);
                    mesh.Explain(special.Name, special.Colour);
                }
                mesh.Line(a + Vector3.UnitZ * Lift, b + Vector3.UnitZ * Lift, Outline, OutlineWidth);
            }
        }
    }

    /// <summary>
    /// A band along an edge, lying on its polygon just above it and running inwards from the edge towards the polygon's
    /// middle, in metres: it is part of the ground it marks, not drawn over it. Narrower than a quarter of the way to
    /// the middle, so a small polygon's bands don't cross.
    /// </summary>
    private static void Ribbon(ViewportMesh mesh, Vector3 a, Vector3 b, Vector3 centroid, float width, uint colour)
    {
        Vector3 along = b - a;
        if (along.LengthSquared() < 1e-8f)
            return;
        along = Vector3.Normalize(along);
        Vector3 middle = (a + b) / 2;
        Vector3 inward = centroid - middle;
        inward -= along * Vector3.Dot(inward, along);
        float room = inward.Length();
        if (room < 1e-4f)
            return;
        inward /= room;
        width = Math.Min(width, room * .25f);
        Vector3 up = Vector3.Cross(along, inward);
        if (up.Z < 0)
            up = -up;
        up = Vector3.Normalize(up) * RibbonLift;
        mesh.Triangle(a + up, b + up, b + inward * width + up, colour, edges: false);
        mesh.Triangle(a + up, b + inward * width + up, a + inward * width + up, colour, edges: false);
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

    // Each portal: a link from one place to another. A ladder is usually two portals between the same two places, one
    // up and one down: they are drawn as one link with an arrowhead at each end, rather than one over the other.
    private static void AddPortals(Ynv navmesh, ViewportMesh mesh)
    {
        var links = new List<(Vector3 From, Vector3 To, bool Ladder, bool Both)>();
        foreach (YnvPortal portal in navmesh.Portals)
        {
            // An end in a neighbouring navmesh has no position until that mesh is loaded.
            if (!portal.IsFromPositionResolved || !portal.IsToPositionResolved)
            {
                mesh.Skip("portal into another navmesh");
                continue;
            }
            Vector3 from = portal.PositionFrom + Vector3.UnitZ * Lift, to = portal.PositionTo + Vector3.UnitZ * Lift;
            bool ladder = portal.Type != YnvPortalType.ClimbObject;
            int reverse = links.FindIndex(link => link.Ladder == ladder && !link.Both
                && Vector3.Distance(link.From, to) < SamePlace && Vector3.Distance(link.To, from) < SamePlace);
            if (reverse >= 0)
                links[reverse] = links[reverse] with { Both = true };
            else
                links.Add((from, to, ladder, false));
        }
        foreach ((Vector3 from, Vector3 to, bool ladder, bool both) in links)
        {
            uint colour = ladder ? Ladder : ClimbObject;
            mesh.Explain(ladder ? "Ladder" : "Climb over", colour);
            Link(mesh, from, to, colour, both);
        }
    }

    /// <summary>
    /// A link as solid shapes, lit like the ground and standing apart from it: a tube from one end to the other,
    /// straight up a ladder and arched over anything else, a cone where it leads (at both ends when it goes both
    /// ways), and a dark puck on the ground at each end. Its sizes follow its length, so a short hop stays tidy.
    /// </summary>
    private static void Link(ViewportMesh mesh, Vector3 from, Vector3 to, uint colour, bool both)
    {
        float length = Vector3.Distance(from, to);
        float tube = Math.Clamp(length * .03f, .035f, .08f);
        float cone = Math.Clamp(length * .2f, .16f, .45f);
        float puck = Math.Clamp(length * .1f, .12f, .28f);

        // Mostly vertical (a ladder) is straight; otherwise an arc whose height grows with the ground it spans. A
        // quadratic Bezier peaks halfway at a quarter of each end plus half the control point: this control point
        // puts the peak `rise` above the higher end.
        float across = new Vector2(to.X - from.X, to.Y - from.Y).Length();
        Vector3 control = (from + to) / 2;
        if (across > Math.Abs(to.Z - from.Z))
        {
            float rise = Math.Clamp(across * .35f, ArcMinimumRise, ArcMaximumRise);
            float peak = Math.Max(from.Z, to.Z) + rise;
            control.Z = 2 * peak - (from.Z + to.Z) / 2;
        }
        Vector3 At(float t) => (1 - t) * (1 - t) * from + 2 * (1 - t) * t * control + t * t * to;
        Vector3 Direction(float t)
        {
            Vector3 derivative = 2 * (1 - t) * (control - from) + 2 * t * (to - control);
            return derivative.LengthSquared() > 1e-12f ? Vector3.Normalize(derivative) : Vector3.Normalize(to - from);
        }

        // The curve measured along its length, so the tube's rings are evenly spaced and its ends cut at a distance.
        const int samples = 64;
        var lengths = new float[samples + 1];
        for (int i = 1; i <= samples; i++)
            lengths[i] = lengths[i - 1] + Vector3.Distance(At((float)(i - 1) / samples), At((float)i / samples));
        float total = lengths[samples];
        float ParameterAt(float distance)
        {
            int i = Array.BinarySearch(lengths, Math.Clamp(distance, 0, total));
            if (i >= 0)
                return (float)i / samples;
            i = ~i;
            float span = lengths[i] - lengths[i - 1];
            return (i - 1 + (span > 0 ? (distance - lengths[i - 1]) / span : 0)) / samples;
        }

        // Each arrowhead stops just clear of the puck it points at: the tube ends a cone's length and that clearance
        // before the end, and the cone carries on straight along the curve's own direction from there, so the tube
        // meets it without a bend.
        float endTrim = cone + Clearance(Direction(1), puck);
        float startTrim = both ? cone + Clearance(-Direction(0), puck) : 0;
        if (endTrim + startTrim > total * .9f)
        {
            float scale = total * .9f / (endTrim + startTrim);
            (endTrim, startTrim, cone) = (endTrim * scale, startTrim * scale, cone * scale);
        }
        float tubeStart = ParameterAt(startTrim), tubeEnd = ParameterAt(total - endTrim);
        var points = new List<Vector3>();
        var directions = new List<Vector3>();
        for (int i = 0; i <= TubeRings; i++)
        {
            float t = ParameterAt(startTrim + (total - endTrim - startTrim) * i / TubeRings);
            points.Add(At(t));
            directions.Add(Direction(t));
        }
        Tube(mesh, points, directions, tube, colour);
        Cone(mesh, At(tubeEnd), At(tubeEnd) + Direction(tubeEnd) * cone, tube * 2.8f, colour);
        if (both)
            Cone(mesh, At(tubeStart), At(tubeStart) - Direction(tubeStart) * cone, tube * 2.8f, colour);
        Puck(mesh, from, puck, PortalBase);
        Puck(mesh, to, puck, PortalBase);
    }

    private const int Sides = 12;
    private const int TubeRings = 24;
    // The space between an arrowhead's tip and the puck it points at.
    private const float TipGap = .04f;

    /// <summary>
    /// How far back from a puck's middle, against the way an arrow arrives, its tip has to stop to clear the puck: out
    /// through the top when it lands from above, the bottom when it comes from below, or the rim when it comes in low.
    /// </summary>
    internal static float Clearance(Vector3 arriving, float puckRadius)
    {
        float top = PuckHeight + TipGap, bottom = Lift * .5f + TipGap;
        float throughFace = arriving.Z < -1e-4f ? top / -arriving.Z : arriving.Z > 1e-4f ? bottom / arriving.Z : float.MaxValue;
        float across = new Vector2(arriving.X, arriving.Y).Length();
        float throughRim = across > 1e-4f ? (puckRadius + TipGap) / across : float.MaxValue;
        return Math.Min(throughFace, throughRim);
    }

    // Rings around the path, each turned to the path's direction there and carried on from the last without twisting,
    // joined by quads, each corner shaded by its own outward normal so the tube reads round.
    private static void Tube(ViewportMesh mesh, List<Vector3> points, List<Vector3> directions, float radius, uint colour)
    {
        if (points.Count < 2)
            return;
        Vector3 normal = Vector3.Cross(directions[0], Vector3.UnitZ);
        normal = normal.LengthSquared() > 1e-6f ? Vector3.Normalize(normal) : Vector3.UnitX;
        Vector3[]? previousRing = null, previousNormals = null;
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 tangent = directions[i];
            Vector3 carried = normal - tangent * Vector3.Dot(normal, tangent);
            normal = carried.LengthSquared() > 1e-8f ? Vector3.Normalize(carried) : normal;
            Vector3 binormal = Vector3.Cross(tangent, normal);
            var ring = new Vector3[Sides];
            var normals = new Vector3[Sides];
            for (int s = 0; s < Sides; s++)
            {
                float angle = s * MathF.Tau / Sides;
                normals[s] = normal * MathF.Cos(angle) + binormal * MathF.Sin(angle);
                ring[s] = points[i] + normals[s] * radius;
            }
            if (previousRing is not null && previousNormals is not null)
            {
                for (int s = 0; s < Sides; s++)
                {
                    int n = (s + 1) % Sides;
                    mesh.Triangle(previousRing[s], ring[s], ring[n], previousNormals[s], normals[s], normals[n], colour);
                    mesh.Triangle(previousRing[s], ring[n], previousRing[n], previousNormals[s], normals[n], previousNormals[n], colour);
                }
            }
            previousRing = ring;
            previousNormals = normals;
        }
    }

    private static void Cone(ViewportMesh mesh, Vector3 baseCenter, Vector3 apex, float radius, uint colour)
    {
        Vector3 axis = Vector3.Normalize(apex - baseCenter);
        Vector3 u = Vector3.Normalize(Vector3.Cross(axis, Math.Abs(axis.Z) < .9f ? Vector3.UnitZ : Vector3.UnitX));
        Vector3 v = Vector3.Cross(axis, u);
        // The side's normal leans towards the apex by the cone's slope.
        float slope = radius / Vector3.Distance(baseCenter, apex);
        for (int s = 0; s < Sides; s++)
        {
            float a = s * MathF.Tau / Sides, b = (s + 1) * MathF.Tau / Sides;
            Vector3 outA = u * MathF.Cos(a) + v * MathF.Sin(a), outB = u * MathF.Cos(b) + v * MathF.Sin(b);
            Vector3 normalA = Vector3.Normalize(outA + axis * slope), normalB = Vector3.Normalize(outB + axis * slope);
            Vector3 normalTip = Vector3.Normalize(normalA + normalB);
            mesh.Triangle(baseCenter + outA * radius, baseCenter + outB * radius, apex, normalA, normalB, normalTip, colour);
            mesh.Triangle(baseCenter + outB * radius, baseCenter + outA * radius, baseCenter, -axis, -axis, -axis, colour);
        }
    }

    // A flat disc with a little thickness, lying on the ground: a smooth rim and a flat top.
    private static void Puck(ViewportMesh mesh, Vector3 center, float radius, uint colour)
    {
        const int sides = 20;
        Vector3 top = center + Vector3.UnitZ * PuckHeight, bottom = center - Vector3.UnitZ * Lift * .5f;
        for (int s = 0; s < sides; s++)
        {
            float a = s * MathF.Tau / sides, b = (s + 1) * MathF.Tau / sides;
            var outA = new Vector3(MathF.Cos(a), MathF.Sin(a), 0);
            var outB = new Vector3(MathF.Cos(b), MathF.Sin(b), 0);
            mesh.Triangle(top, top + outA * radius, top + outB * radius, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, colour);
            mesh.Triangle(top + outA * radius, bottom + outA * radius, bottom + outB * radius, outA, outA, outB, colour);
            mesh.Triangle(top + outA * radius, bottom + outB * radius, top + outB * radius, outA, outB, outB, colour);
        }
    }

    // Where a ped takes cover: a dark puck where it stands, and in front of it, the way it faces, a panel as high as the
    // cover it gets there, low or a whole wall.
    private static void AddCoverPoints(Ynv navmesh, ViewportMesh mesh)
    {
        foreach (YnvPoint point in navmesh.Points)
            AddCoverPoint(mesh, point.Position, point.Direction, point.Type, point.Disabled);
    }

    /// <summary>One cover point, at a position, facing a heading in radians.</summary>
    internal static void AddCoverPoint(ViewportMesh mesh, Vector3 position, float direction, YnvPointType type, bool disabled)
    {
        bool low = type is YnvPointType.LowWall or YnvPointType.LowWallToLeft or YnvPointType.LowWallToRight;
        (string name, uint colour) = disabled ? ("Cover point, disabled", DisabledCover)
            : low ? ("Cover point, low", LowCover)
            : ("Cover point, wall", WallCover);
        mesh.Explain(name, colour);
        Vector3 foot = position + Vector3.UnitZ * Lift;
        // The heading turns from north (+Y) towards west, as the game's headings do.
        var facing = new Vector3(-MathF.Sin(direction), MathF.Cos(direction), 0);
        Puck(mesh, foot, CoverPuck, PortalBase);
        Panel(mesh, foot + facing * CoverReach, facing, low ? LowCoverHeight : WallCoverHeight, colour);
    }

    // An upright slab standing on the ground at `bottom`, its broad faces looking along `facing`.
    private static void Panel(ViewportMesh mesh, Vector3 bottom, Vector3 facing, float height, uint colour)
    {
        Vector3 across = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, facing)) * (PanelWidth / 2);
        Vector3 depth = facing * (PanelThickness / 2), up = Vector3.UnitZ * height;
        Vector3[] c =
        [
            bottom - across - depth, bottom + across - depth, bottom + across + depth, bottom - across + depth,
            bottom - across - depth + up, bottom + across - depth + up, bottom + across + depth + up, bottom - across + depth + up,
        ];
        void Face(int a, int b, int d, int e)
        {
            mesh.Triangle(c[a], c[b], c[d], colour, edges: false);
            mesh.Triangle(c[a], c[d], c[e], colour, edges: false);
        }
        Face(0, 1, 5, 4);
        Face(1, 2, 6, 5);
        Face(2, 3, 7, 6);
        Face(3, 0, 4, 7);
        Face(4, 5, 6, 7);
    }

    private static uint Pack(string hex)
    {
        var colour = Avalonia.Media.Color.Parse(hex);
        return ViewportMesh.Pack(colour.R, colour.G, colour.B);
    }
}
