using HikariHopper.Rendering;

namespace HikariHopper.Tests;

/// <summary>The GPU the rendering tests draw on; a test is skipped on a machine without one.</summary>
internal static class TestGpu
{
    internal static GpuDevice Device()
    {
        try
        {
            return GpuDevice.Shared;
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException or DllNotFoundException
            or WebGpuSharp.WebGPUException)
        {
            Assert.Skip($"No GPU to test on: {error.Message}");
            throw;
        }
    }
}
