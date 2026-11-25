using HikariHopper.Gpu;
using Buffer = HikariHopper.Gpu.Buffer;

namespace HikariHopper.Rendering.World;

/// <summary>A range of a GPU buffer: where an asset's vertices, indexes, placements or material values are.</summary>
internal readonly record struct WorldSlice(Buffer Buffer, ulong Offset, ulong Size);

/// <summary>
/// The world's buffers of one usage, shared: ranges of a few large pages, each range 256-byte aligned (as a uniform or
/// storage binding's offset must be), a large range a buffer of its own. D3D12 gives every buffer at least 64 KB, so
/// the world's small buffers (a material's values, a prop's placements) took several times the memory they hold while
/// each had its own.
/// </summary>
/// <remarks>
/// A page, and a buffer of its own, is bigger than 4 MB, so that destroying it gives its memory back. Dawn places
/// every buffer and texture up to 4 MB in heaps of its own that it keeps for the device's life, each rounded up to a
/// power of two: such a buffer destroyed leaves the process holding what it held (the heap is only used again), and
/// what the world's assets let go never came off what the system showed. A bigger one is an allocation of its own,
/// freed with it.
/// </remarks>
internal sealed class WorldBufferPages(Device device, BufferUsage usage, ulong pageBytes = WorldBufferPages.DefaultPageBytes)
{
    internal const ulong DefaultPageBytes = 8 * 1024 * 1024;
    internal const ulong Alignment = 256;

    private sealed class Page(Buffer buffer, ulong size, bool own)
    {
        internal readonly Buffer Buffer = buffer;
        internal readonly ulong Size = size;

        // A range bigger than half a page: a buffer of its own, destroyed when it's let go.
        internal readonly bool Own = own;

        // What's free, by offset, adjacent ranges joined.
        internal readonly List<(ulong Offset, ulong Size)> Free = own ? [] : [(0, size)];
        internal ulong Used;

        // Whether emptying it was tried and something it holds had nowhere to go: left alone until room is freed.
        internal bool Stuck;
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
        if (rounded > pageBytes / 2)
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

    /// <summary>
    /// The page to empty into the others: the one holding the least (the last of those, as the earlier take from the
    /// later), when the fuller ones have room for what it holds. Null when there's none. The ranges the assets let go
    /// are scattered over the pages, most of which keep something: without moving what's left together, hardly a page
    /// was ever empty to give back.
    /// </summary>
    internal Buffer? Sparsest()
    {
        Page? found = null;
        foreach (Page page in pages)
            if (!page.Own && page.Used > 0 && !page.Stuck && (found is null || page.Used <= found.Used))
                found = page;
        if (found is null)
            return null;
        ulong room = 0;
        for (int i = 0; i < pages.Count; i++)
            if (Takes(pages[i], i, found))
                room += pages[i].Size - pages[i].Used;
        return room >= found.Used ? found.Buffer : null;
    }

    /// <summary>
    /// A range of at least <paramref name="size"/> bytes in a page fuller than <paramref name="from"/>, for what that
    /// one holds to move to; null when none has the room (no page is made for it).
    /// </summary>
    internal WorldSlice? Move(ulong size, Buffer from)
    {
        if (!byBuffer.TryGetValue(from, out Page? source))
            return null;
        ulong rounded = (size + Alignment - 1) / Alignment * Alignment;
        for (int i = 0; i < pages.Count; i++)
            if (Takes(pages[i], i, source) && pages[i].Size - pages[i].Used >= rounded && Take(pages[i], rounded) is { } offset)
                return new WorldSlice(pages[i].Buffer, offset, size);
        return null;
    }

    // Whether a page takes what another holds: only a fuller one does (of two as full, the earlier), so ranges only
    // ever move towards the fuller pages and the moving ends. Into an emptier one, the two would trade ranges for ever.
    private bool Takes(Page page, int index, Page from) =>
        !page.Own && !ReferenceEquals(page, from) && (page.Used > from.Used || page.Used == from.Used && index < pages.IndexOf(from));

    /// <summary>Leaves a page that couldn't be emptied alone, until a range is given back.</summary>
    internal void Stick(Buffer buffer)
    {
        if (byBuffer.TryGetValue(buffer, out Page? page))
            page.Stuck = true;
    }

    /// <summary>Gives a range back; a page nothing uses any more goes, but for one kept for what comes next.</summary>
    internal void Free(WorldSlice slice)
    {
        if (!byBuffer.TryGetValue(slice.Buffer, out Page? page))
            return;
        ulong rounded = (slice.Size + Alignment - 1) / Alignment * Alignment;
        UsedBytes -= rounded;
        // There's room that wasn't there when a page was last found stuck.
        foreach (Page other in pages)
            other.Stuck = false;
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
        if (page.Used == 0 && AnotherEmpty(page))
            Remove(page);
    }

    // Whether a page other than this one holds nothing: one empty page is enough to keep.
    private bool AnotherEmpty(Page page)
    {
        foreach (Page other in pages)
            if (!other.Own && other.Used == 0 && !ReferenceEquals(other, page))
                return true;
        return false;
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
