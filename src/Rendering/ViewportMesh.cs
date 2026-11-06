using System.Numerics;
using System.Runtime.InteropServices;

namespace HikariHopper.Rendering;

/// <summary>One corner of a triangle as the GPU reads it: where it is, which way its face looks, and its colour.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal readonly record struct ViewportVertex(Vector3 Position, Vector3 Normal, uint Colour)
{
    public const int Size = 28;
}

/// <summary>
/// Triangles for the viewport, each with corners of its own so faces stay flat and its edges can be drawn. Positions
/// are relative to <see cref="Origin"/>, the middle of what is drawn: content placed far out in the world stays precise.
/// </summary>
internal sealed class ViewportMesh(Vector3 origin)
{
    private readonly List<ViewportVertex> vertices = [];

    public Vector3 Origin { get; } = origin;

    public IReadOnlyList<ViewportVertex> Vertices => vertices;

    public int TriangleCount => vertices.Count / 3;

    /// <summary>Shapes that couldn't be drawn, by kind, and how many.</summary>
    public Dictionary<string, int> Skipped { get; } = [];

    public ViewportVertex[] ToArray() => [.. vertices];

    /// <summary>A triangle at world positions, in an sRGB colour; one with no area is left out.</summary>
    public void Triangle(Vector3 a, Vector3 b, Vector3 c, uint colour)
    {
        Vector3 normal = Vector3.Cross(b - a, c - a);
        if (normal.LengthSquared() <= 1e-12f || !float.IsFinite(normal.LengthSquared()))
            return;
        normal = Vector3.Normalize(normal);
        vertices.Add(new ViewportVertex(a - Origin, normal, colour));
        vertices.Add(new ViewportVertex(b - Origin, normal, colour));
        vertices.Add(new ViewportVertex(c - Origin, normal, colour));
    }

    public void Skip(string kind) => Skipped[kind] = Skipped.GetValueOrDefault(kind) + 1;

    /// <summary>An sRGB colour packed as the GPU's 8-bit RGBA, full alpha.</summary>
    public static uint Pack(byte r, byte g, byte b) => r | (uint)g << 8 | (uint)b << 16 | 0xFF000000;
}
