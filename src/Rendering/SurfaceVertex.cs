using System.Numerics;
using System.Runtime.InteropServices;

namespace HikariHopper.Rendering;

/// <summary>
/// One corner of a drawable's surface, as surface.wgsl reads it: everything GTA's material shaders take from a vertex.
/// Positions are relative to the scene's origin.
/// </summary>
/// <param name="Tangent">The tangent, with the binormal's handedness in W (the binormal is cross(T, N) · W).</param>
/// <param name="Colour0">RGBA, R first: natural ambient, artificial ambient, emissive mask, alpha.</param>
/// <param name="Colour1">RGBA, R first; tints and blends for the shaders that use it.</param>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal readonly record struct SurfaceVertex(Vector3 Position, Vector3 Normal, Vector4 Tangent, Vector2 Uv0, Vector2 Uv1, uint Colour0, uint Colour1)
{
    public const int Size = 64;

    /// <summary>A colour as four bytes, R first.</summary>
    public static uint Pack(Vector4 colour)
    {
        Vector4 c = Vector4.Clamp(colour, Vector4.Zero, Vector4.One) * 255 + new Vector4(.5f);
        return (uint)c.X | (uint)c.Y << 8 | (uint)c.Z << 16 | (uint)c.W << 24;
    }
}
