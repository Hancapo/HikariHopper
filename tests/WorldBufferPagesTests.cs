using HikariHopper.Rendering.World;
using WebGpuSharp;

namespace HikariHopper.Tests;

public sealed class WorldBufferPagesTests
{
    private const ulong Page = 64 * 1024;

    [Fact]
    public void SmallRangesShareAPageAlignedAndApart()
    {
        var gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            var pages = new WorldBufferPages(gpu.Device, BufferUsage.Uniform | BufferUsage.CopyDst, Page);
            WorldSlice[] slices = [.. Enumerable.Range(0, 40).Select(i => pages.Allocate(400))];
            Assert.Equal(1, pages.Buffers);
            Assert.All(slices, slice => Assert.Equal(0UL, slice.Offset % WorldBufferPages.Alignment));
            Assert.All(slices, slice => Assert.Equal(400UL, slice.Size));
            Assert.Equal(40, slices.Select(slice => slice.Offset).Distinct().Count());
            // 512 bytes each (400 rounded up to the alignment): none overlaps the next.
            ulong[] offsets = [.. slices.Select(slice => slice.Offset).Order()];
            for (int i = 1; i < offsets.Length; i++)
                Assert.True(offsets[i] - offsets[i - 1] >= 512);
            Assert.Equal(40UL * 512, pages.UsedBytes);
            pages.Clear();
        }
    }

    [Fact]
    public void FreedRangesJoinAndAreUsedAgain()
    {
        var gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            var pages = new WorldBufferPages(gpu.Device, BufferUsage.Vertex | BufferUsage.CopyDst, Page);
            WorldSlice a = pages.Allocate(4096), b = pages.Allocate(4096), c = pages.Allocate(4096);
            pages.Free(a);
            pages.Free(b);
            // The two freed neighbours make one range that takes a range of both their sizes.
            WorldSlice joined = pages.Allocate(8192);
            Assert.Equal(a.Offset, joined.Offset);
            Assert.Same(a.Buffer, joined.Buffer);
            Assert.Equal(1, pages.Buffers);
            pages.Free(joined);
            pages.Free(c);
            Assert.Equal(0UL, pages.UsedBytes);
            pages.Clear();
        }
    }

    [Fact]
    public void TheEmptiestPageMovesIntoTheFullerOnesUntilNoneTakesMore()
    {
        var gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            var pages = new WorldBufferPages(gpu.Device, BufferUsage.Vertex | BufferUsage.CopyDst | BufferUsage.CopySrc, Page);
            // Three pages of four ranges each, then holes: the first keeps three, the second one, the third two.
            WorldSlice[] slices = [.. Enumerable.Range(0, 12).Select(_ => pages.Allocate(Page / 4))];
            Assert.Equal(3, pages.Buffers);
            foreach (int gone in new[] { 3, 5, 6, 7, 10, 11 })
                pages.Free(slices[gone]);
            // The second, holding least, is the one to empty, and only into a fuller page: the first, which has the room.
            Assert.Same(slices[4].Buffer, pages.Sparsest());
            WorldSlice moved = pages.Move(Page / 4, slices[4].Buffer)!.Value;
            Assert.Same(slices[0].Buffer, moved.Buffer);
            pages.Free(slices[4]);
            // Emptied, it's the page kept for what comes next. The third's two ranges have no fuller page with room for
            // both: the first is full, and an emptier page takes nothing, or the two would trade ranges for ever.
            Assert.Equal(3, pages.Buffers);
            Assert.Null(pages.Sparsest());
            Assert.Null(pages.Move(Page / 4, slices[8].Buffer));
            // Room in the first again: the third can go, a range at a time, and its page with it.
            pages.Free(slices[0]);
            pages.Free(slices[1]);
            Assert.Same(slices[8].Buffer, pages.Sparsest());
            foreach (int index in new[] { 8, 9 })
            {
                Assert.Same(slices[2].Buffer, pages.Move(Page / 4, slices[index].Buffer)!.Value.Buffer);
                pages.Free(slices[index]);
            }
            Assert.Equal(2, pages.Buffers);
            Assert.Null(pages.Sparsest());
            pages.Clear();
        }
    }

    [Fact]
    public void ALargeRangeIsABufferOfItsOwnAndEmptyPagesGo()
    {
        var gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            var pages = new WorldBufferPages(gpu.Device, BufferUsage.Index | BufferUsage.CopyDst, Page);
            // Half a page still shares one; more is a buffer of its own, of its size.
            WorldSlice half = pages.Allocate(Page / 2);
            Assert.Equal(Page, pages.Bytes);
            pages.Free(half);
            pages.Clear();
            WorldSlice large = pages.Allocate(Page / 2 + 256);
            Assert.Equal(0UL, large.Offset);
            Assert.Equal(Page / 2 + 256, pages.Bytes);
            pages.Free(large);
            Assert.Equal(0, pages.Buffers);
            // Two pages' worth of ranges, then all of them back: one empty page is kept for what comes next.
            WorldSlice[] slices = [.. Enumerable.Range(0, 8).Select(_ => pages.Allocate(Page / 4))];
            Assert.Equal(2, pages.Buffers);
            foreach (WorldSlice slice in slices)
                pages.Free(slice);
            Assert.Equal(1, pages.Buffers);
            Assert.Equal(0UL, pages.UsedBytes);
            pages.Clear();
            Assert.Equal(0, pages.Buffers);
        }
    }
}
