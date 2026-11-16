using WebGpuSharp;
using Buffer = WebGpuSharp.Buffer;

namespace HikariHopper.Rendering.World;

/// <summary>A range of a GPU buffer: where an asset's vertices, indexes, placements or material values are.</summary>
internal readonly record struct WorldSlice(Buffer Buffer, ulong Offset, ulong Size);

/// <summary>
/// The world's buffers of one usage, shared: ranges of a few large pages, each range 256-byte aligned (as a uniform or
/// storage binding's offset must be), a large range a buffer of its own. D3D12 gives every buffer at least 64 KB, so
/// the world's small buffers (a material's values, a prop's placements) took several times the memory they hold while
/// each had its own.
/// </summary>
internal sealed class WorldBufferPages(Device device, BufferUsage usage, ulong pageBytes = WorldBufferPages.DefaultPageBytes)
{
    internal const ulong DefaultPageBytes = 4 * 1024 * 1024;
    internal const ulong Alignment = 256;

    private sealed class Page(Buffer buffer, ulong size, bool own)
    {
        internal readonly Buffer Buffer = buffer;
        internal readonly ulong Size = size;

        // A range bigger than a quarter of a page: a buffer of its own, destroyed when it's let go.
        internal readonly bool Own = own;

        // What's free, by offset, adjacent ranges joined.
        internal readonly List<(ulong Offset, ulong Size)> Free = own ? [] : [(0, size)];
        internal ulong Used;
    }

    private readonly List<Page> pages = [];
    private readonly Dictionary<Buffer, Page> byBuffer = new(ReferenceEqualityComparer.Instance);

    /// <summary>The GPU memory the pages take, used or not.</summary>
    internal ulong Bytes { get; private set; }

    /// <summary>What the ranges given out hold.</summary>
    internal ulong UsedBytes { get; private set; }

    /// <summary>How many buffers the pages are.</summary>
    internal int Buffers => byBuffer.Count;

    /// <summary>A range of at least <paramref name="size"/> bytes, the first free one that fits, or a new page's.</summary>
    internal WorldSlice Allocate(ulong size)
    {
        ArgumentOutOfRangeException.ThrowIfZero(size);
        ulong rounded = (size + Alignment - 1) / Alignment * Alignment;
        if (rounded > pageBytes / 4)
        {
            var own = new Page(device.CreateBuffer(new BufferDescriptor { Size = rounded, Usage = usage }), rounded, own: true) { Used = rounded };
            Add(own);
            UsedBytes += rounded;
            return new WorldSlice(own.Buffer, 0, size);
        }
        foreach (Page page in pages)
            if (!page.Own && page.Size - page.Used >= rounded && Take(page, rounded) is { } offset)
                return new WorldSlice(page.Buffer, offset, size);
        var fresh = new Page(device.CreateBuffer(new BufferDescriptor { Size = pageBytes, Usage = usage }), pageBytes, own: false);
        Add(fresh);
        return new WorldSlice(fresh.Buffer, Take(fresh, rounded)!.Value, size);
    }

    /// <summary>Gives a range back; a page nothing uses any more goes, but for one kept for what comes next.</summary>
    internal void Free(WorldSlice slice)
    {
        if (!byBuffer.TryGetValue(slice.Buffer, out Page? page))
            return;
        ulong rounded = (slice.Size + Alignment - 1) / Alignment * Alignment;
        UsedBytes -= rounded;
        if (page.Own)
        {
            Remove(page);
            return;
        }
        page.Used -= rounded;
        int at = page.Free.FindIndex(range => range.Offset > slice.Offset);
        if (at < 0)
            at = page.Free.Count;
        page.Free.Insert(at, (slice.Offset, rounded));
        // Joined to the range after it, then to the one before.
        if (at + 1 < page.Free.Count && page.Free[at].Offset + page.Free[at].Size == page.Free[at + 1].Offset)
        {
            page.Free[at] = (page.Free[at].Offset, page.Free[at].Size + page.Free[at + 1].Size);
            page.Free.RemoveAt(at + 1);
        }
        if (at > 0 && page.Free[at - 1].Offset + page.Free[at - 1].Size == page.Free[at].Offset)
        {
            page.Free[at - 1] = (page.Free[at - 1].Offset, page.Free[at - 1].Size + page.Free[at].Size);
            page.Free.RemoveAt(at);
        }
        if (page.Used == 0 && pages.Count(p => !p.Own && p.Used == 0) > 1)
            Remove(page);
    }

    /// <summary>Destroys every page.</summary>
    internal void Clear()
    {
        foreach (Page page in pages)
            page.Buffer.Destroy();
        pages.Clear();
        byBuffer.Clear();
        Bytes = UsedBytes = 0;
    }

    private ulong? Take(Page page, ulong size)
    {
        for (int i = 0; i < page.Free.Count; i++)
        {
            var (offset, free) = page.Free[i];
            if (free < size)
                continue;
            if (free == size)
                page.Free.RemoveAt(i);
            else
                page.Free[i] = (offset + size, free - size);
            page.Used += size;
            UsedBytes += size;
            return offset;
        }
        return null;
    }

    private void Add(Page page)
    {
        pages.Add(page);
        byBuffer.Add(page.Buffer, page);
        Bytes += page.Size;
    }

    private void Remove(Page page)
    {
        pages.Remove(page);
        byBuffer.Remove(page.Buffer);
        Bytes -= page.Size;
        page.Buffer.Destroy();
    }
}
