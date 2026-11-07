using HikariHopper.Rendering;

namespace HikariHopper.Tests;

public sealed class GpuDeviceTests
{
    [Fact]
    public async Task Warming_up_compiles_the_viewport_pipelines_once_for_every_viewport()
    {
        GpuDevice gpu;
        try
        {
            gpu = GpuDevice.Shared;
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException or DllNotFoundException
            or WebGpuSharp.WebGPUException)
        {
            Assert.Skip($"No GPU to test on: {error.Message}");
            return;
        }
        await GpuDevice.WarmUp();
        // A window's usual format: the pipelines a viewport needs are ready before one opens.
        foreach (string key in new[] { "backdrop", "meshes", "lines", "shadow", "ground-shadow", "tonemap", "luminance",
            "luminance-mean", "reflection-map/fs_reflection_map", "surface/Back/False/False", "surface/None/True/True" })
            Assert.True(gpu.Compiled($"{key}/{WebGpuSharp.TextureFormat.BGRA8UnormSrgb}"), key);
        // Warming up again does nothing more, and viewports share the device.
        Assert.Same(GpuDevice.WarmUp(), GpuDevice.WarmUp());
        Assert.Same(gpu, GpuDevice.Shared);
    }
}
