using System.Numerics;
using System.Runtime.InteropServices;

namespace HikariHopper.Rendering;

/// <summary>
/// One corner of a triangle as the GPU reads it: where it is, which way its face looks, and its colour. The colour's
/// alpha is how strongly the triangle's own edges are drawn: none where a polygon was split into triangles.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal readonly record struct ViewportVertex(Vector3 Position, Vector3 Normal, uint Colour)
{
    public const int Size = 28;
}

/// <summary>A line from A to B, drawn this many pixels wide whatever its distance, in its colour.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal readonly record struct ViewportSegment(Vector3 A, Vector3 B, uint Colour, float Width)
{
    public const int Size = 32;
}

/// <summary>A colour the viewer explains, what it means, and how many things wear it.</summary>
internal sealed record LegendEntry(string Name, uint Colour, int Count);

/// <summary>
/// What a mesh's triangles and lines are marked as, by colour: a collision's material, a navmesh's kind of ground.
/// The parts panel lists the marks, under their sections, and shows or hides what wears each.
/// </summary>
/// <param name="Index">Which of the mesh's marks it is.</param>
/// <param name="Section">The heading it is listed under: Materials, Ground.</param>
/// <param name="Count">How many things wear it: polygons, edges, cover points.</param>
/// <param name="Triangles">The triangles drawn for them.</param>
/// <param name="Lines">The lines drawn for them.</param>
internal sealed record MeshMark(int Index, string Section, string Name, uint Colour, int Count, int Triangles, int Lines);

/// <summary>
/// Triangles and lines for the viewport, each triangle with corners of its own so faces stay flat and its edges can be
/// drawn. Positions are relative to <see cref="Origin"/>, the middle of what is drawn: content placed far out in the
/// world stays precise. What is drawn can be marked as one of the things the mesh explains, and a mark's triangles
/// and lines hidden.
/// </summary>
internal sealed class ViewportMesh(Vector3 origin)
{
    private readonly List<ViewportVertex> vertices = [];
    private readonly List<ViewportSegment> segments = [];
    // The marks, in the order first used; how many things wear each; and each triangle's and line's mark, -1 for none.
    private readonly List<(string Section, string Name, uint Colour)> marks = [];
    private readonly Dictionary<(string Name, uint Colour), int> markOf = [];
    private readonly List<int> counts = [];
    private readonly List<int> triangleMarks = [];
    private readonly List<int> segmentMarks = [];
    private readonly HashSet<int> hidden = [];
    private int current = -1;

    public Vector3 Origin { get; } = origin;

    public IReadOnlyList<ViewportVertex> Vertices => vertices;

    public IReadOnlyList<ViewportSegment> Segments => segments;

    public int TriangleCount => vertices.Count / 3;

    /// <summary>Shapes that couldn't be drawn, by kind, and how many.</summary>
    public Dictionary<string, int> Skipped { get; } = [];

    /// <summary>The colours in use and what they mean, in the order first used.</summary>
    public IReadOnlyList<LegendEntry> Legend =>
        [.. marks.Select((mark, index) => new LegendEntry(mark.Name, mark.Colour, counts[index])).Where(entry => entry.Count > 0)];

    /// <summary>The marks that have something drawn, in the order first used.</summary>
    public IReadOnlyList<MeshMark> Marks
    {
        get
        {
            var triangles = new int[marks.Count];
            var lines = new int[marks.Count];
            foreach (int mark in triangleMarks)
                if (mark >= 0)
                    triangles[mark]++;
            foreach (int mark in segmentMarks)
                if (mark >= 0)
                    lines[mark]++;
            return [.. marks.Select((mark, index) => new MeshMark(index, mark.Section, mark.Name, mark.Colour, counts[index], triangles[index], lines[index]))
                .Where(mark => mark.Triangles > 0 || mark.Lines > 0)];
        }
    }

    /// <summary>The triangles' corners, less those of the marks hidden.</summary>
    public ViewportVertex[] ToArray()
    {
        if (hidden.Count == 0)
            return [.. vertices];
        var shown = new List<ViewportVertex>(vertices.Count);
        for (int triangle = 0; triangle < triangleMarks.Count; triangle++)
            if (!hidden.Contains(triangleMarks[triangle]))
                shown.AddRange(vertices.GetRange(triangle * 3, 3));
        return [.. shown];
    }

    /// <summary>The lines, less those of the marks hidden.</summary>
    public ViewportSegment[] SegmentArray() =>
        hidden.Count == 0 ? [.. segments] : [.. segments.Where((_, index) => !hidden.Contains(segmentMarks[index]))];

    /// <summary>Whether a mark's triangles and lines are drawn.</summary>
    public bool Shown(int mark) => !hidden.Contains(mark);

    /// <summary>Draws a mark's triangles and lines, or leaves them out.</summary>
    public void Show(int mark, bool shown)
    {
        if (mark < 0 || mark >= marks.Count)
            return;
        if (shown)
            hidden.Remove(mark);
        else
            hidden.Add(mark);
    }

    /// <summary>Draws every mark's.</summary>
    public void ShowAll() => hidden.Clear();

    /// <summary>The world box around what these marks wear; none when nothing does.</summary>
    public CommonFury.Numerics.Aabb3? BoundsOf(IEnumerable<int> wanted)
    {
        HashSet<int> set = [.. wanted];
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        bool any = false;
        void Take(Vector3 position)
        {
            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
            any = true;
        }
        for (int triangle = 0; triangle < triangleMarks.Count; triangle++)
            if (set.Contains(triangleMarks[triangle]))
                for (int corner = 0; corner < 3; corner++)
                    Take(vertices[triangle * 3 + corner].Position);
        for (int line = 0; line < segmentMarks.Count; line++)
            if (set.Contains(segmentMarks[line]))
            {
                Take(segments[line].A);
                Take(segments[line].B);
            }
        return any ? new CommonFury.Numerics.Aabb3(min + Origin, max + Origin) : null;
    }

    /// <summary>
    /// A triangle at world positions, in an sRGB colour; with its edges drawn, or not when it is part of a larger
    /// polygon. One with no area is left out.
    /// </summary>
    public void Triangle(Vector3 a, Vector3 b, Vector3 c, uint colour, bool edges = true)
    {
        Vector3 normal = Vector3.Cross(b - a, c - a);
        if (normal.LengthSquared() <= 1e-12f || !float.IsFinite(normal.LengthSquared()))
            return;
        normal = Vector3.Normalize(normal);
        if (!edges)
            colour &= 0x00FFFFFF;
        vertices.Add(new ViewportVertex(a - Origin, normal, colour));
        vertices.Add(new ViewportVertex(b - Origin, normal, colour));
        vertices.Add(new ViewportVertex(c - Origin, normal, colour));
        triangleMarks.Add(current);
    }

    /// <summary>
    /// A triangle of a smooth surface: each corner with its own normal, so shading flows across the surface's faces
    /// instead of breaking at them. Its edges aren't drawn.
    /// </summary>
    public void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 normalA, Vector3 normalB, Vector3 normalC, uint colour)
    {
        // NaN fails every comparison, so the test is written to fail it: no area, or a corner or normal that isn't
        // finite (a curve of no length), is left out.
        float area = Vector3.Cross(b - a, c - a).LengthSquared();
        if (!(area > 1e-14f) || !float.IsFinite(area)
            || !float.IsFinite((normalA + normalB + normalC).LengthSquared()))
            return;
        colour &= 0x00FFFFFF;
        vertices.Add(new ViewportVertex(a - Origin, normalA, colour));
        vertices.Add(new ViewportVertex(b - Origin, normalB, colour));
        vertices.Add(new ViewportVertex(c - Origin, normalC, colour));
        triangleMarks.Add(current);
    }

    /// <summary>A line between world positions, in an sRGB colour, this many pixels wide.</summary>
    public void Line(Vector3 a, Vector3 b, uint colour, float width)
    {
        if (float.IsFinite(a.LengthSquared()) && float.IsFinite(b.LengthSquared()))
        {
            segments.Add(new ViewportSegment(a - Origin, b - Origin, colour, width));
            segmentMarks.Add(current);
        }
    }

    /// <summary>
    /// Counts one more thing in this colour for the legend, and gives its mark; <paramref name="section"/> is the
    /// heading the parts panel lists it under, the first one given for a name and colour.
    /// </summary>
    public int Explain(string name, uint colour, string section = "")
    {
        int mark = MarkOf(name, colour, section);
        counts[mark]++;
        return mark;
    }

    /// <summary>What is drawn from here on wears this mark, without counting anything yet; gives the mark.</summary>
    public int Mark(string name, uint colour, string section = "")
    {
        current = MarkOf(name, colour, section);
        return current;
    }

    /// <summary>What is drawn from here on wears this mark; -1 for none, which nothing hides.</summary>
    public void Mark(int mark) => current = mark >= 0 && mark < marks.Count ? mark : -1;

    private int MarkOf(string name, uint colour, string section)
    {
        if (markOf.TryGetValue((name, colour), out int mark))
            return mark;
        marks.Add((section, name, colour));
        counts.Add(0);
        return markOf[(name, colour)] = marks.Count - 1;
    }

    public void Skip(string kind) => Skipped[kind] = Skipped.GetValueOrDefault(kind) + 1;

    /// <summary>An sRGB colour packed as the GPU's 8-bit RGBA, full alpha.</summary>
    public static uint Pack(byte r, byte g, byte b) => r | (uint)g << 8 | (uint)b << 16 | 0xFF000000;

    /// <summary>A theme colour packed as the GPU's 8-bit RGBA, full alpha.</summary>
    public static uint Pack(Avalonia.Media.IImmutableSolidColorBrush brush) => Pack(brush.Color.R, brush.Color.G, brush.Color.B);

    /// <summary>A packed colour back as a theme colour, for the legend.</summary>
    public static Avalonia.Media.Color Unpack(uint colour) => Avalonia.Media.Color.FromRgb((byte)colour, (byte)(colour >> 8), (byte)(colour >> 16));
}
