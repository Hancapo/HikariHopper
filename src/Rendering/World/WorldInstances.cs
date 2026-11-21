using System.Numerics;

namespace HikariHopper.Rendering.World;

/// <summary>
/// An asset's placements as Rdr2/world.wgsl reads them, each its world matrix (from the asset's own vertices) and its
/// inverse transpose, a row each (after RedFury's WorldInstanceBuffer). The renderer keeps each covered node's rows
/// where they are, and writes to the GPU only the rows it set.
/// </summary>
internal sealed class WorldInstanceBuffer
{
    internal Matrix4x4[] Matrices { get; private set; } = [];

    /// <summary>The rows in use, gaps included.</summary>
    internal int Count { get; private set; }

    /// <summary>Sets a row's world matrix and inverse transpose, growing the rows to reach it.</summary>
    internal void Set(int row, in Matrix4x4 world, in Matrix4x4 normal)
    {
        int offset = row * 2;
        if (offset + 2 > Matrices.Length)
        {
            Matrix4x4[] grown = Matrices;
            Array.Resize(ref grown, Math.Max(32, Math.Max(offset + 2, Matrices.Length * 2)));
            Matrices = grown;
        }
        Matrices[offset] = world;
        Matrices[offset + 1] = normal;
        Count = Math.Max(Count, row + 1);
    }

    /// <summary>The rows from <paramref name="rows"/> on are no longer kept.</summary>
    internal void Truncate(int rows) => Count = Math.Min(Count, rows);
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
