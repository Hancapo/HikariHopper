using HikariHopper.Rendering;

namespace HikariHopper.Tests;

public sealed class GpuDeviceTests
{
    [Fact]
    public async Task Warming_up_compiles_the_viewport_pipelines_once_for_every_viewport()
    {
        GpuDevice gpu = TestGpu.Device();
        await GpuDevice.WarmUp();
        // A window's usual format: the pipelines a viewport needs are ready before one opens.
        foreach (string key in new[] { "backdrop", "meshes", "lines", "shadow", "shadow-masked", "ground-shadow", "tonemap", "luminance",
            "luminance-mean", "reflection-map/fs_reflection_map", "surface/Back/False/False", "surface/None/True/True",
            "terrain/Back/False/False", "terrain/None/False/True", "fur/None/False/False", "fur/None/False/True", "cable/None/True/False",
            "bloom/fs_bloom_bright", "bloom/fs_bloom_downsample_karis",
            "bloom/fs_bloom_downsample", "bloom/fs_bloom_upsample", "outline-mask", "outline" })
            Assert.True(gpu.Compiled($"{key}/{ViewportRenderer.FrameFormat}"), key);
        // CMAA2 over the frame, and the frame to a window in its usual format.
        foreach (string entry in new[] { "cs_edges", "cs_args_candidates", "cs_process_candidates", "cs_args_apply", "cs_apply" })
            Assert.True(gpu.Compiled($"cmaa2/{entry}"), entry);
        Assert.True(gpu.Compiled($"present/{HikariHopper.Gpu.TextureFormat.BGRA8UnormSrgb}"));
        // Both games' world pipelines, compiled or compiling.
        foreach (string key in new[] { "gta-world/Back/False/False", "gta-terrain-world/None/True/True", "gta-water-world/None/True/False",
            "rdr2-world/Back/False/False", "rdr2-water-world/None/True/False" })
            Assert.True(gpu.Compiled($"{key}/{ViewportRenderer.FrameFormat}"), key);
        Assert.True(gpu.Compiled("gta-world-shadow-masked") && gpu.Compiled("rdr2-world-shadow-masked"));
        // Warming up again does nothing more, and viewports share the device.
        Assert.Same(GpuDevice.WarmUp(), GpuDevice.WarmUp());
        Assert.Same(gpu, GpuDevice.Shared);
    }

    [Fact]
    public async Task A_pipeline_compiling_in_the_background_is_waited_for_and_one_that_failed_there_compiled_again()
    {
        GpuDevice gpu = TestGpu.Device();
        await GpuDevice.WarmUp();
        lock (gpu.Gate)
        {
            // A viewport's pipelines, compiled (again, if another test forgot them).
            new ViewportRenderer(gpu).Dispose();
            RenderPipelineOf(gpu, out HikariHopper.Gpu.RenderPipeline compiled);
            gpu.Prepare("test-background", () => Task.Run(async () =>
            {
                await Task.Delay(50);
                return compiled;
            }));
            Assert.Same(compiled, gpu.Pipeline("test-background", () => throw new InvalidOperationException("Compiled again.")));
            gpu.Prepare("test-failed", () => Task.FromException<HikariHopper.Gpu.RenderPipeline>(new InvalidOperationException("Failed.")));
            Assert.Same(compiled, gpu.Pipeline("test-failed", () => compiled));
            // Taken once, it's compiled: preparing it again starts nothing.
            gpu.Prepare("test-background", () => throw new InvalidOperationException("Started again."));
        }

        // One a viewport compiled.
        static void RenderPipelineOf(GpuDevice gpu, out HikariHopper.Gpu.RenderPipeline pipeline) =>
            pipeline = gpu.Pipeline($"tonemap/{ViewportRenderer.FrameFormat}", () => throw new InvalidOperationException("Not compiled."));
    }

    [Fact]
    public void Every_viewport_shader_and_pipeline_is_valid()
    {
        GpuDevice gpu = TestGpu.Device();
        // A shader that doesn't compile still makes a pipeline, an invalid one; the device only says so in an error scope.
        (HikariHopper.Gpu.ErrorType Type, string Message) result;
        lock (gpu.Gate)
        {
            gpu.ForgetPipelines();
            gpu.Device.PushErrorScope(HikariHopper.Gpu.ErrorFilter.Validation);
            new ViewportRenderer(gpu).Dispose();
            result = gpu.Device.PopErrorScopeSync(10_000_000_000);
        }
        Assert.True(result.Type == HikariHopper.Gpu.ErrorType.NoError, $"{result.Type}: {result.Message}");
    }

    [Fact]
    public void Cmaa2_blends_the_pixels_along_hard_edges_from_their_neighbours_and_leaves_the_rest()
    {
        GpuDevice gpu = TestGpu.Device();
        const int Size = 128;
        byte[] plain, smoothed;
        (HikariHopper.Gpu.ErrorType Type, string Message) result;
        lock (gpu.Gate)
        {
            gpu.Device.PushErrorScope(HikariHopper.Gpu.ErrorFilter.Validation);
            var renderer = new ViewportRenderer(gpu);
            renderer.Resize(Size, Size);
            // A white triangle across the view, its long side slanting gently: stair steps for CMAA2 to find.
            var mesh = new ViewportMesh(System.Numerics.Vector3.Zero);
            mesh.Triangle(new(-3, -3, 0), new(3, -2, 0), new(-1, 3, 0), ViewportMesh.Pack(255, 255, 255), edges: false);
            renderer.Show(mesh);
            renderer.Frame(new CommonFury.Numerics.Aabb3(new(-3, -3, 0), new(3, 3, 0)));
            HikariHopper.Gpu.Texture target = gpu.Device.CreateTexture(new HikariHopper.Gpu.TextureDescriptor
            {
                Usage = HikariHopper.Gpu.TextureUsage.RenderAttachment | HikariHopper.Gpu.TextureUsage.CopySrc,
                Dimension = HikariHopper.Gpu.TextureDimension.D2,
                Size = new HikariHopper.Gpu.Extent3D(Size, Size, 1),
                Format = renderer.PresentFormat,
                MipLevelCount = 1,
                SampleCount = 1,
            });
            HikariHopper.Gpu.TextureView view = target.CreateView();
            byte[] Draw(bool antialiasing)
            {
                renderer.Antialiasing = antialiasing;
                renderer.RenderTo(view);
                HikariHopper.Gpu.Buffer readback = gpu.Device.CreateBuffer(new HikariHopper.Gpu.BufferDescriptor
                {
                    Size = Size * Size * 4,
                    Usage = HikariHopper.Gpu.BufferUsage.CopyDst | HikariHopper.Gpu.BufferUsage.MapRead,
                });
                HikariHopper.Gpu.CommandEncoder encoder = gpu.Device.CreateCommandEncoder();
                encoder.CopyTextureToBuffer(new HikariHopper.Gpu.TexelCopyTextureInfo { Texture = target },
                    new HikariHopper.Gpu.TexelCopyBufferInfo { Buffer = readback, Layout = new HikariHopper.Gpu.TexelCopyBufferLayout { BytesPerRow = Size * 4, RowsPerImage = Size } },
                    new HikariHopper.Gpu.Extent3D(Size, Size, 1));
                gpu.Queue.Submit(encoder.Finish());
                readback.MapSync(HikariHopper.Gpu.MapMode.Read);
                byte[] pixels = readback.GetConstMappedRange<byte[], byte>(0, Size * Size * 4, span => span.ToArray());
                readback.Unmap();
                readback.Destroy();
                return pixels;
            }
            plain = Draw(antialiasing: false);
            smoothed = Draw(antialiasing: true);
            target.Destroy();
            renderer.Dispose();
            result = gpu.Device.PopErrorScopeSync(10_000_000_000);
        }
        Assert.True(result.Type == HikariHopper.Gpu.ErrorType.NoError, $"{result.Type}: {result.Message}");

        int changed = 0;
        for (int y = 1; y < Size - 1; y++)
            for (int x = 1; x < Size - 1; x++)
                for (int c = 0; c < 3; c++)
                {
                    int at = (y * Size + x) * 4 + c;
                    if (Math.Abs(smoothed[at] - plain[at]) <= 1)
                        continue;
                    changed++;
                    // A blend of the pixel and its neighbours: within their range, give or take rounding.
                    int low = 255, high = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int value = plain[((y + dy) * Size + x + dx) * 4 + c];
                            low = Math.Min(low, value);
                            high = Math.Max(high, value);
                        }
                    Assert.InRange(smoothed[at], low - 2, high + 2);
                }
        // Some of the edge's pixels, and nothing like the whole image.
        Assert.True(changed > 20, $"{changed} channels changed");
        Assert.True(changed < Size * Size * 3 / 8, $"{changed} channels changed");
    }
}
