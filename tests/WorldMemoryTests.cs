using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using HikariHopper.Core;
using HikariHopper.Rendering;
using HikariHopper.Rendering.World;
using RedFury.World;

namespace HikariHopper.Tests;

// What a world gives back once the camera has left: the assets its cut no longer asks for, the buffer pages they leave
// half empty, and their memory to the system.
public sealed class WorldMemoryTests
{
    private const int Width = 64, Height = 32;

    // A view of the origin from a few metres south, with nothing in it that changes from one frame to the next.
    private static (ViewportRenderer Renderer, HikariHopper.Gpu.Texture Target, HikariHopper.Gpu.TextureView View) Scene(GpuDevice gpu)
    {
        var renderer = new ViewportRenderer(gpu);
        HikariHopper.Gpu.Texture target = gpu.Device.CreateTexture(new HikariHopper.Gpu.TextureDescriptor
        {
            Usage = HikariHopper.Gpu.TextureUsage.RenderAttachment | HikariHopper.Gpu.TextureUsage.CopySrc,
            Dimension = HikariHopper.Gpu.TextureDimension.D2,
            Size = new HikariHopper.Gpu.Extent3D(Width, Height, 1),
            Format = renderer.PresentFormat,
            MipLevelCount = 1,
            SampleCount = 1,
        });
        renderer.Resize(Width, Height);
        renderer.Antialiasing = false;
        renderer.Bloom = false;
        renderer.AmbientOcclusion = false;
        renderer.WorldShown = true;
        renderer.Camera.Frame(new Vector3(0, -5, 1), 20);
        renderer.Camera.Free = true;
        renderer.Camera.Place(new Vector3(0, -5, 1), -MathF.PI / 2, 0);
        renderer.SetTime(13);
        return (renderer, target, target.CreateView());
    }

    // A triangle uploaded as an asset, with a texture of its own colour when it's given one, and a placement of it.
    private static WorldLodNode Place(ViewportRenderer renderer, uint hash, Vector3 at, byte[]? colour = null)
    {
        byte[] ydr = colour is null ? TestDrawables.Triangle() : TestDrawables.Triangle(diffuse: Textures.TextureTestKit.Bgra($"own_{hash}", 4, 4, colour));
        FiveFury.Drawables.Drawable drawable = FiveFury.Ydr.Ydr.Read(ydr).Drawable;
        var key = new WorldAssetKey(hash, 2);
        Assert.True(renderer.AddWorldAsset(WorldAsset.FromGta(key, DrawableScene.Build(drawable, []), [drawable]), new HashSet<WorldAssetKey>(), long.MaxValue));
        while (renderer.HasPendingWorldUpload)
            renderer.ProcessWorldUploads();
        return new WorldLodNode(new WorldEntityId(1, (int)hash), null, key, Matrix4x4.CreateTranslation(at), at + new Vector3(.5f, .5f, 0), 1, 100, 0, 0);
    }

    private static void FadesEnd(ViewportRenderer renderer, HikariHopper.Gpu.TextureView view)
    {
        Thread.Sleep(TimeSpan.FromSeconds(ViewportRenderer.LodFadeSeconds + .15));
        renderer.RenderTo(view);
        Assert.Equal(0, renderer.WorldFadingOut);
    }

    [Fact]
    public void Assets_the_cut_left_behind_go_once_they_have_waited_and_those_it_asks_for_or_draws_stay()
    {
        GpuDevice gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            var (renderer, target, view) = Scene(gpu);
            gpu.Device.PushErrorScope(HikariHopper.Gpu.ErrorFilter.Validation);
            WorldLodNode a = Place(renderer, 1, new Vector3(0, 0, 0), [200, 20, 20, 255]), b = Place(renderer, 2, new Vector3(1, 0, 0), [20, 200, 20, 255]),
                c = Place(renderer, 3, new Vector3(2, 0, 0), [20, 20, 200, 255]);
            long second = Stopwatch.Frequency;
            // The cut asks for all three, and covers two.
            renderer.KeepWorldAssets([a.Asset, b.Asset, c.Asset], 0);
            renderer.SetWorldSelection(new WorldSelection([a, b], [a.Asset, b.Asset, c.Asset]));
            renderer.RenderTo(view);
            Assert.Equal(3, renderer.WorldTextureCount);
            long charged = renderer.WorldBytes;
            ulong used = renderer.WorldBufferMemory.Used;
            // Ten seconds on it asks for the first alone. Nothing has waited thirty seconds yet.
            renderer.KeepWorldAssets([a.Asset], 10);
            renderer.SetWorldSelection(new WorldSelection([a], [a.Asset]));
            renderer.RenderTo(view);
            Assert.Equal(0, renderer.QueueWorldReleases(10 - 30));
            Assert.Equal(0, renderer.ReleaseWorldAssets(second));
            // Thirty seconds after it last asked for the other two, they're lined up. The one never drawn goes; the one
            // still fading out of the view stays for now.
            renderer.KeepWorldAssets([a.Asset], 31);
            Assert.Equal(2, renderer.QueueWorldReleases(31 - 30));
            Assert.Equal(1, renderer.ReleaseWorldAssets(second));
            Assert.False(renderer.IsWorldReady(c.Asset));
            Assert.True(renderer.IsWorldReady(b.Asset));
            // Faded out, it goes at the next line-up; the one asked for stays, whatever the time.
            FadesEnd(renderer, view);
            renderer.KeepWorldAssets([a.Asset], 32);
            Assert.Equal(1, renderer.QueueWorldReleases(32 - 30));
            Assert.Equal(1, renderer.ReleaseWorldAssets(second));
            Assert.False(renderer.IsWorldReady(b.Asset));
            renderer.KeepWorldAssets([a.Asset], 1000);
            Assert.Equal(0, renderer.QueueWorldReleases(1000 - 30));
            Assert.True(renderer.IsWorldReady(a.Asset));
            Assert.Equal(2, renderer.WorldReleased);
            // With them went their textures, their ranges of the pages and their charge.
            Assert.Equal(1, renderer.WorldTextureCount);
            Assert.True(renderer.WorldBytes < charged);
            Assert.True(renderer.WorldBufferMemory.Used < used);
            // One lined up that the cut asks for again before its turn stays.
            WorldLodNode d = Place(renderer, 4, new Vector3(3, 0, 0));
            renderer.KeepWorldAssets([a.Asset], 2000);
            Assert.Equal(1, renderer.QueueWorldReleases(1500));
            renderer.KeepWorldAssets([a.Asset, d.Asset], 2001);
            Assert.Equal(0, renderer.ReleaseWorldAssets(second));
            Assert.True(renderer.IsWorldReady(d.Asset));
            renderer.RenderTo(view);
            Assert.True(renderer.WorldSourcesMatchCoverage());
            var (type, message) = gpu.Device.PopErrorScopeSync(5_000_000_000);
            Assert.True(type == HikariHopper.Gpu.ErrorType.NoError, message);
            renderer.Dispose();
            target.Destroy();
        }
    }

    [Fact]
    public void A_texture_its_last_asset_lets_go_of_stays_for_the_asset_on_its_way_up()
    {
        GpuDevice gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            var (renderer, target, view) = Scene(gpu);
            gpu.Device.PushErrorScope(HikariHopper.Gpu.ErrorFilter.Validation);
            // Two assets reading the same dictionary's texture, each its own read of it.
            WorldAsset Read(uint hash)
            {
                var dictionary = new FiveFury.Textures.TextureDictionary();
                dictionary.Textures.Add(new FiveFury.Textures.Texture("road_d", 4, 4, CommonFury.Textures.TextureFormat.BC1Unorm, 1, new byte[8]));
                FiveFury.Drawables.Drawable drawable = FiveFury.Ydr.Ydr.Read(TestDrawables.Triangle(diffuseName: "road_d")).Drawable;
                return WorldAsset.FromGta(new WorldAssetKey(hash, 2), DrawableScene.Build(drawable, [dictionary]), [drawable], read => new WorldTextureId(7, 9));
            }
            Assert.True(renderer.AddWorldAsset(Read(1), new HashSet<WorldAssetKey>(), long.MaxValue));
            while (renderer.HasPendingWorldUpload)
                renderer.ProcessWorldUploads();
            Assert.Equal(1, renderer.WorldTextureCount);
            // The second starts on its way up, finding the texture there; the first, left behind, goes before it's up.
            Assert.True(renderer.AddWorldAsset(Read(2), new HashSet<WorldAssetKey>(), long.MaxValue));
            Assert.True(renderer.HasPendingWorldUpload);
            renderer.KeepWorldAssets([], 100);
            Assert.Equal(1, renderer.QueueWorldReleases(50));
            Assert.Equal(1, renderer.ReleaseWorldAssets(Stopwatch.Frequency));
            Assert.False(renderer.IsWorldReady(new WorldAssetKey(1, 2)));
            Assert.Equal(1, renderer.WorldTextureCount);
            while (renderer.HasPendingWorldUpload)
                renderer.ProcessWorldUploads();
            // Up, the second binds it; it goes with it.
            Assert.True(renderer.IsWorldReady(new WorldAssetKey(2, 2)));
            Assert.Equal(1, renderer.WorldTextureCount);
            renderer.KeepWorldAssets([], 200);
            Assert.Equal(1, renderer.QueueWorldReleases(150));
            Assert.Equal(1, renderer.ReleaseWorldAssets(Stopwatch.Frequency));
            Assert.Equal(0, renderer.WorldTextureCount);
            renderer.RenderTo(view);
            var (type, message) = gpu.Device.PopErrorScopeSync(5_000_000_000);
            Assert.True(type == HikariHopper.Gpu.ErrorType.NoError, message);
            renderer.Dispose();
            target.Destroy();
        }
    }

    [Fact]
    public void What_is_left_in_half_empty_pages_moves_to_the_fuller_ones_and_the_frame_stays_the_same()
    {
        GpuDevice gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            var (renderer, target, view) = Scene(gpu);
            // Pages of a few assets each, so two dozen triangles take several.
            renderer.WorldPageBytes = 4096;
            renderer.Shadows = false;
            gpu.Device.PushErrorScope(HikariHopper.Gpu.ErrorFilter.Validation);
            WorldLodNode[] nodes = [.. Enumerable.Range(0, 24).Select(i => Place(renderer, (uint)i + 1, new Vector3(i * .25f - 3, 0, 0), [(byte)(i * 10), 120, 60, 255]))];
            WorldAssetKey[] keys = [.. nodes.Select(node => node.Asset)];
            renderer.KeepWorldAssets(keys, 0);
            renderer.SetWorldSelection(new WorldSelection(nodes, keys));
            renderer.RenderTo(view);
            int pages = renderer.WorldBufferMemory.Buffers;
            // One in four stays; the others are let go, leaving every page mostly empty.
            WorldLodNode[] kept = [.. nodes.Where((_, i) => i % 4 == 0)];
            WorldAssetKey[] keptKeys = [.. kept.Select(node => node.Asset)];
            renderer.KeepWorldAssets(keptKeys, 100);
            renderer.SetWorldSelection(new WorldSelection(kept, keptKeys));
            renderer.RenderTo(view);
            FadesEnd(renderer, view);
            Assert.Equal(18, renderer.QueueWorldReleases(50));
            Assert.Equal(18, renderer.ReleaseWorldAssets(Stopwatch.Frequency));
            renderer.RenderTo(view);
            renderer.RenderTo(view);
            byte[] before = Read(gpu, target);
            var (bytes, used, sparse) = renderer.WorldBufferMemory;
            Assert.True(sparse > 2 && sparse <= pages, $"{sparse} pages of {pages} left");
            // Moved a little a call, as the frames do it, until nothing more moves.
            int calls = 0;
            while (renderer.CompactWorldBuffers(2048, 2) > 0)
            {
                renderer.RenderTo(view);
                Assert.True(++calls < 1000, "The pages never settle.");
            }
            Assert.True(renderer.WorldBytesCompacted > 0);
            Assert.Equal(0, renderer.CompactWorldBuffers(long.MaxValue, int.MaxValue));
            var (packedBytes, packedUsed, packed) = renderer.WorldBufferMemory;
            Assert.Equal(used, packedUsed);
            Assert.True(packed < sparse && packedBytes < bytes, $"{packed} pages ({packedBytes} bytes) from {sparse} ({bytes})");
            // What's drawn reads its vertices, its indexes and its placements where they are now.
            renderer.RenderTo(view);
            renderer.RenderTo(view);
            Assert.True(renderer.WorldSourcesMatchCoverage());
            Assert.Equal(before, Read(gpu, target));
            Assert.Contains(before, value => value != before[0]);
            var (type, message) = gpu.Device.PopErrorScopeSync(5_000_000_000);
            Assert.True(type == HikariHopper.Gpu.ErrorType.NoError, message);
            renderer.Dispose();
            target.Destroy();
        }
    }

    // The frame's pixels.
    private static byte[] Read(GpuDevice gpu, HikariHopper.Gpu.Texture target)
    {
        const uint Row = 256;
        HikariHopper.Gpu.Buffer readback = gpu.Device.CreateBuffer(new HikariHopper.Gpu.BufferDescriptor
        {
            Size = Row * Height,
            Usage = HikariHopper.Gpu.BufferUsage.CopyDst | HikariHopper.Gpu.BufferUsage.MapRead,
        });
        HikariHopper.Gpu.CommandEncoder encoder = gpu.Device.CreateCommandEncoder();
        encoder.CopyTextureToBuffer(new HikariHopper.Gpu.TexelCopyTextureInfo { Texture = target },
            new HikariHopper.Gpu.TexelCopyBufferInfo { Buffer = readback, Layout = new HikariHopper.Gpu.TexelCopyBufferLayout { BytesPerRow = Row, RowsPerImage = Height } },
            new HikariHopper.Gpu.Extent3D(Width, Height, 1));
        gpu.Queue.Submit(encoder.Finish());
        readback.MapSync(HikariHopper.Gpu.MapMode.Read);
        byte[] pixels = readback.GetConstMappedRange<byte[], byte>(0, Row * Height, span => span.ToArray());
        readback.Unmap();
        readback.Destroy();
        return pixels;
    }

    // Two places far apart, each with a model of its own, read as often as it's asked for.
    private sealed class TwoPlaces(WorldLodGraph lod) : WorldIndex(Path.GetTempPath(), 0, lod)
    {
        internal readonly ConcurrentDictionary<WorldAssetKey, int> Reads = new();

        public override IReadOnlyList<WorldFamily> Families => [WorldFamily.Gta];

        public override WorldAsset LoadAsset(WorldAssetKey key)
        {
            Reads.AddOrUpdate(key, 1, (_, count) => count + 1);
            FiveFury.Drawables.Drawable drawable = FiveFury.Ydr.Ydr.Read(TestDrawables.Triangle()).Drawable;
            return WorldAsset.FromGta(key, DrawableScene.Build(drawable, []), [drawable]);
        }

        public override Func<WorldLodNode, CancellationToken, WorldLodNode>? CreateGrass() => null;

        public override WorldFile? Locate(uint hash, byte extension) => null;

        public override string NameOf(uint hash) => hash.ToString("X8");
    }

    [Fact]
    public void A_model_left_behind_is_let_go_after_the_wait_and_read_again_when_the_camera_comes_back()
    {
        GpuDevice gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            var (renderer, target, view) = Scene(gpu);
            Vector3 here = new(0, 0, 0), there = new(5000, 0, 0);
            WorldLodNode near = new(new WorldEntityId(1, 0), null, new WorldAssetKey(1, 2), Matrix4x4.CreateTranslation(here), here, 1, 100, 0, 0),
                far = new(new WorldEntityId(1, 1), null, new WorldAssetKey(2, 2), Matrix4x4.CreateTranslation(there), there, 1, 100, 0, 0);
            var index = new TwoPlaces(new WorldLodGraph([near, far]));
            using var streamer = new WorldStreamer(index);
            // A tenth of a second of streaming a frame, until what's waited for is so.
            void Frames(Func<bool> done, string what)
            {
                var clock = Stopwatch.StartNew();
                while (!done())
                {
                    Assert.True(clock.Elapsed < TimeSpan.FromSeconds(60), $"Timed out waiting for {what}.");
                    streamer.Update(renderer, .1f, 13);
                    renderer.RenderTo(view);
                    Thread.Sleep(2);
                }
            }
            void Go(Vector3 at) => renderer.Camera.Place(at + new Vector3(0, -5, 1), -MathF.PI / 2, 0);
            Go(here);
            Frames(() => renderer.IsWorldReady(near.Asset), "the model here");
            Assert.False(renderer.IsWorldReady(far.Asset));
            // The camera goes far away: the model there comes in, and the one left behind stays while it waits.
            streamer.ReleaseAfter = 1e9;
            Go(there);
            Frames(() => renderer.IsWorldReady(far.Asset), "the model there");
            Assert.True(renderer.IsWorldReady(near.Asset));
            Assert.Equal(0, renderer.WorldReleased);
            // Its wait over, it's let go; the one the cut asks for isn't.
            streamer.ReleaseAfter = 3;
            Frames(() => !renderer.IsWorldReady(near.Asset), "the model left behind to go");
            Assert.True(renderer.IsWorldReady(far.Asset));
            Assert.Equal(1, renderer.WorldReleased);
            Assert.Equal(1, index.Reads[near.Asset]);
            // Back where it was, it's read and put up again.
            Go(here);
            Frames(() => renderer.IsWorldReady(near.Asset), "the model here again");
            Assert.Equal(2, index.Reads[near.Asset]);
            renderer.Dispose();
            target.Destroy();
        }
    }

    // wgpu's own count of the memory its device's buffers and textures hold.
    private static ulong DeviceBytes(GpuDevice gpu)
    {
        gpu.Device.Poll(wait: true);
        var (buffers, textures, allocations) = gpu.Device.MemoryCounters();
        if (allocations == 0 && buffers == 0)
            Assert.Skip("This build of wgpu doesn't count what its device holds.");
        return (ulong)(buffers + textures);
    }

    [Fact]
    public void A_page_emptied_gives_its_memory_back_to_the_system()
    {
        GpuDevice gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            const ulong Megabyte = 1024 * 1024;
            // What other renderers destroyed is gone before counting.
            Submit(gpu);
            ulong before = DeviceBytes(gpu);
            var pages = new WorldBufferPages(gpu.Device, HikariHopper.Gpu.BufferUsage.Vertex | HikariHopper.Gpu.BufferUsage.CopyDst);
            // Four pages' worth of ranges, each written so the memory is there.
            byte[] data = new byte[2 * Megabyte];
            WorldSlice[] slices = [.. Enumerable.Range(0, (int)(4 * WorldBufferPages.DefaultPageBytes / (2 * Megabyte))).Select(_ => pages.Allocate(2 * Megabyte))];
            foreach (WorldSlice slice in slices)
                gpu.Queue.WriteBuffer(slice.Buffer, slice.Offset, (ReadOnlySpan<byte>)data);
            Submit(gpu);
            Assert.Equal(4, pages.Buffers);
            ulong held = DeviceBytes(gpu);
            Assert.True(held >= before + 4 * WorldBufferPages.DefaultPageBytes, $"{before} then {held}");
            // All given back: one page is kept for what comes next, and the system has the others' memory again.
            foreach (WorldSlice slice in slices)
                pages.Free(slice);
            Assert.Equal(1, pages.Buffers);
            Submit(gpu);
            ulong after = DeviceBytes(gpu);
            Assert.True(after <= held - 3 * WorldBufferPages.DefaultPageBytes, $"{held} then {after}");
            pages.Clear();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HeapSummaryInfo
    {
        public uint Size;
        public nuint Allocated, Committed, Reserved, MaxReserve;
    }

    [DllImport("kernel32")]
    private static extern uint GetProcessHeaps(uint count, nint[] heaps);

    [DllImport("kernel32")]
    private static extern int HeapSummary(nint heap, uint flags, ref HeapSummaryInfo summary);

    // What's allocated in the process's own heaps, where Dawn keeps what isn't the GPU's.
    private static long HeapBytes()
    {
        var heaps = new nint[256];
        uint count = GetProcessHeaps((uint)heaps.Length, heaps);
        long allocated = 0;
        for (int i = 0; i < Math.Min(count, heaps.Length); i++)
        {
            var summary = new HeapSummaryInfo { Size = (uint)Marshal.SizeOf<HeapSummaryInfo>() };
            if (HeapSummary(heaps[i], 0, ref summary) != 0)
                allocated += (long)summary.Allocated;
        }
        return allocated;
    }

    [Fact]
    public void A_bundle_let_go_leaves_none_of_its_memory_behind()
    {
        GpuDevice gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            HikariHopper.Gpu.Buffer buffer = gpu.Device.CreateBuffer(new HikariHopper.Gpu.BufferDescriptor { Size = 4096, Usage = HikariHopper.Gpu.BufferUsage.Vertex });
            // Each bundle keeps 16 KB for its commands: these would leave 128 MB behind were they never freed.
            const int Count = 8000;
            long before = HeapBytes();
            for (int i = 0; i < Count; i++)
            {
                HikariHopper.Gpu.RenderBundleEncoder encoder = gpu.Device.CreateRenderBundleEncoder(new HikariHopper.Gpu.RenderBundleEncoderDescriptor
                {
                    ColorFormats = [HikariHopper.Gpu.TextureFormat.RGBA8Unorm],
                    DepthStencilFormat = HikariHopper.Gpu.TextureFormat.Depth32Float,
                    SampleCount = 1,
                });
                encoder.SetVertexBuffer(0, buffer, 0, 4096);
                encoder.Finish().Dispose();
            }
            long left = HeapBytes() - before;
            Assert.True(left < 32L * 1024 * 1024, $"{left:N0} bytes left by {Count} bundles");
            buffer.Destroy();
        }
    }

    // What's destroyed goes once the GPU is done with the commands before it.
    private static void Submit(GpuDevice gpu)
    {
        for (int i = 0; i < 20; i++)
        {
            gpu.Queue.Submit(gpu.Device.CreateCommandEncoder().Finish());
            Thread.Sleep(10);
        }
    }
}
