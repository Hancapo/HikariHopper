using System.Numerics;

namespace HikariHopper.Rendering.World;

/// <summary>
/// An asset's placements as rdr2_world.wgsl reads them, each its world matrix (from the asset's own vertices) and its
/// inverse transpose (RedFury's WorldInstanceBuffer, copied). Kept while the coverage stays; whether it changed is
/// known, so only changed ones are written to the GPU.
/// </summary>
internal sealed class WorldInstanceBuffer
{
    internal Matrix4x4[] Matrices { get; private set; } = [];
    internal int Count { get; private set; }
    internal bool Changed { get; private set; }

    internal void Begin()
    {
        Count = 0;
        Changed = false;
    }

    /// <summary>A placement's world matrix and its inverse transpose, from its prepared block.</summary>
    internal void Add(in Matrix4x4 world, in Matrix4x4 normal)
    {
        int offset = Count * 2;
        if (offset + 2 > Matrices.Length)
        {
            Matrix4x4[] grown = Matrices;
            Array.Resize(ref grown, Math.Max(32, offset * 2));
            Matrices = grown;
        }
        if (Matrices[offset] != world || Matrices[offset + 1] != normal)
        {
            Matrices[offset] = world;
            Matrices[offset + 1] = normal;
            Changed = true;
        }
        Count++;
    }
}

/// <summary>
/// The indexes of an asset's placements in view (RedFury's WorldInstanceIndices, copied): what moves each frame, four
/// bytes a placement. The count says which are live; old tails are reused.
/// </summary>
internal sealed class WorldInstanceIndices
{
    internal uint[] Values { get; private set; } = [];
    internal int Count { get; private set; }
    internal bool Changed { get; private set; }

    internal void Reserve(int count)
    {
        if (count <= Values.Length)
            return;
        uint[] values = Values;
        Array.Resize(ref values, count);
        Values = values;
    }

    internal void Begin()
    {
        Count = 0;
        Changed = false;
    }

    internal void Add(uint source)
    {
        if (Count == Values.Length)
            Reserve(Math.Max(16, checked(Count * 2)));
        if (Values[Count] != source)
        {
            Values[Count] = source;
            Changed = true;
        }
        Count++;
    }
}
