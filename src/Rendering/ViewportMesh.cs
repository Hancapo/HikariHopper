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
/// Triangles and lines for the viewport, each triangle with corners of its own so faces stay flat and its edges can be
/// drawn. Positions are relative to <see cref="Origin"/>, the middle of what is drawn: content placed far out in the
/// world stays precise.
/// </summary>
internal sealed class ViewportMesh(Vector3 origin)
{
    private readonly List<ViewportVertex> vertices = [];
    private readonly List<ViewportSegment> segments = [];
    private readonly Dictionary<(string Name, uint Colour), int> legend = [];

    public Vector3 Origin { get; } = origin;

    public IReadOnlyList<ViewportVertex> Vertices => vertices;

    public IReadOnlyList<ViewportSegment> Segments => segments;

    public int TriangleCount => vertices.Count / 3;

    /// <summary>Shapes that couldn't be drawn, by kind, and how many.</summary>
    public Dictionary<string, int> Skipped { get; } = [];

    /// <summary>The colours in use and what they mean, in the order first used.</summary>
    public IReadOnlyList<LegendEntry> Legend => [.. legend.Select(entry => new LegendEntry(entry.Key.Name, entry.Key.Colour, entry.Value))];

    public ViewportVertex[] ToArray() => [.. vertices];

    public ViewportSegment[] SegmentArray() => [.. segments];

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
    }

    /// <summary>
    /// A triangle of a smooth surface: each corner with its own normal, so shading flows across the surface's faces
    /// instead of breaking at them. Its edges aren't drawn.
    /// </summary>
    public void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 normalA, Vector3 normalB, Vector3 normalC, uint colour)
    {
        if (Vector3.Cross(b - a, c - a).LengthSquared() <= 1e-14f)
            return;
        colour &= 0x00FFFFFF;
        vertices.Add(new ViewportVertex(a - Origin, normalA, colour));
        vertices.Add(new ViewportVertex(b - Origin, normalB, colour));
        vertices.Add(new ViewportVertex(c - Origin, normalC, colour));
    }

    /// <summary>A line between world positions, in an sRGB colour, this many pixels wide.</summary>
    public void Line(Vector3 a, Vector3 b, uint colour, float width)
    {
        if (float.IsFinite(a.LengthSquared()) && float.IsFinite(b.LengthSquared()))
            segments.Add(new ViewportSegment(a - Origin, b - Origin, colour, width));
    }

    /// <summary>Counts one more thing in this colour for the legend.</summary>
    public void Explain(string name, uint colour) => legend[(name, colour)] = legend.GetValueOrDefault((name, colour)) + 1;

    public void Skip(string kind) => Skipped[kind] = Skipped.GetValueOrDefault(kind) + 1;

    /// <summary>An sRGB colour packed as the GPU's 8-bit RGBA, full alpha.</summary>
    public static uint Pack(byte r, byte g, byte b) => r | (uint)g << 8 | (uint)b << 16 | 0xFF000000;

    /// <summary>A theme colour packed as the GPU's 8-bit RGBA, full alpha.</summary>
    public static uint Pack(Avalonia.Media.IImmutableSolidColorBrush brush) => Pack(brush.Color.R, brush.Color.G, brush.Color.B);

    /// <summary>A packed colour back as a theme colour, for the legend.</summary>
    public static Avalonia.Media.Color Unpack(uint colour) => Avalonia.Media.Color.FromRgb((byte)colour, (byte)(colour >> 8), (byte)(colour >> 16));
}
