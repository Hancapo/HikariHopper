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
            "luminance-mean", "reflection-map/fs_reflection_map", "surface/Back/False/False", "surface/None/True/True",
            "terrain/Back/False/False", "terrain/None/False/True" })
            Assert.True(gpu.Compiled($"{key}/{WebGpuSharp.TextureFormat.BGRA8UnormSrgb}"), key);
        // Warming up again does nothing more, and viewports share the device.
        Assert.Same(GpuDevice.WarmUp(), GpuDevice.WarmUp());
        Assert.Same(gpu, GpuDevice.Shared);
    }

    [Fact]
    public void Every_viewport_shader_and_pipeline_is_valid()
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
        // A shader that doesn't compile still makes a pipeline, an invalid one; the device only says so in an error scope.
        (WebGpuSharp.ErrorType Type, string Message) result;
        lock (gpu.Gate)
        {
            gpu.ForgetPipelines();
            gpu.Device.PushErrorScope(WebGpuSharp.ErrorFilter.Validation);
            new ViewportRenderer(gpu).Dispose();
            result = gpu.Device.PopErrorScopeSync(10_000_000_000);
        }
        Assert.True(result.Type == WebGpuSharp.ErrorType.NoError, $"{result.Type}: {result.Message}");
    }
}
