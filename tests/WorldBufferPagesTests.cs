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
    public void ALargeRangeIsABufferOfItsOwnAndEmptyPagesGo()
    {
        var gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            var pages = new WorldBufferPages(gpu.Device, BufferUsage.Index | BufferUsage.CopyDst, Page);
            WorldSlice large = pages.Allocate(Page / 2);
            Assert.Equal(0UL, large.Offset);
            Assert.Equal(Page / 2, pages.Bytes);
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
