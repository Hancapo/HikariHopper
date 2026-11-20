using System.Numerics;

namespace HikariHopper.Rendering;

/// <summary>
/// The vertices and indexes a scene is built into, sized for all of them before the first goes in: neither grows (and
/// is copied) as it fills, nor is copied once more when it's done. The worlds build hundreds of models a second, and
/// lists that doubled as they filled, then were copied out, allocated most of what their building did.
/// </summary>
internal sealed class SceneGeometry(int vertices, int indices)
{
    private SurfaceVertex[] vertices = vertices == 0 ? [] : new SurfaceVertex[vertices];
    private uint[] indices = indices == 0 ? [] : new uint[indices];

    // A stamp a vertex at a time, for telling which of a geometry's vertices a part uses without a set.
    private int[] marks = [];
    private int stamp;

    public int VertexCount { get; private set; }

    public int IndexCount { get; private set; }

    public SurfaceVertex this[int vertex] => vertices[vertex];

    public void Add(in SurfaceVertex vertex)
    {
        if (VertexCount == vertices.Length)
            Array.Resize(ref vertices, Math.Max(16, vertices.Length * 2));
        vertices[VertexCount++] = vertex;
    }

    /// <summary>Room for <paramref name="count"/> more indexes, at the end: they're the next ones.</summary>
    public Span<uint> AddIndices(int count)
    {
        if (IndexCount + count > indices.Length)
            Array.Resize(ref indices, Math.Max(IndexCount + count, indices.Length * 2));
        Span<uint> added = indices.AsSpan(IndexCount, count);
        IndexCount += count;
        return added;
    }

    /// <summary>The indexes from <paramref name="first"/>, <paramref name="count"/> of them.</summary>
    public Span<uint> Indices(int first, int count) => indices.AsSpan(first, count);

    /// <summary>
    /// How many distinct vertices a part's indexes use (they count from <paramref name="baseVertex"/>, a geometry of
    /// <paramref name="geometryVertices"/>), and their middle.
    /// </summary>
    public (int Count, Vector3 Middle) Used(int first, int count, int baseVertex, int geometryVertices)
    {
        if (marks.Length < geometryVertices)
            marks = new int[Math.Max(geometryVertices, marks.Length * 2)];
        if (++stamp == int.MaxValue)
        {
            Array.Clear(marks);
            stamp = 1;
        }
        int used = 0;
        Vector3 sum = Vector3.Zero;
        foreach (uint index in indices.AsSpan(first, count))
            if (index < (uint)geometryVertices && marks[index] != stamp)
            {
                marks[index] = stamp;
                sum += vertices[baseVertex + (int)index].Position;
                used++;
            }
        return (used, used > 0 ? sum / used : Vector3.Zero);
    }

    /// <summary>The vertices: the array they were built in when it came out the size foreseen.</summary>
    public SurfaceVertex[] Vertices() => VertexCount == vertices.Length ? vertices : vertices[..VertexCount];

    /// <summary>The indexes, likewise.</summary>
    public uint[] AllIndices() => IndexCount == indices.Length ? indices : indices[..IndexCount];
}
