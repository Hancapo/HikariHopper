using System.Runtime.InteropServices;
using HikariHopper.Rendering;
using HikariHopper.Gpu;

namespace HikariHopper.Tests;

public sealed class ViewportRecoveryTests
{
    private const SurfaceGetCurrentTextureStatus Optimal = SurfaceGetCurrentTextureStatus.SuccessOptimal;
    private const SurfaceGetCurrentTextureStatus Outdated = SurfaceGetCurrentTextureStatus.Outdated;
    private const SurfaceGetCurrentTextureStatus Timeout = SurfaceGetCurrentTextureStatus.Timeout;

    [Fact]
    public void A_frame_with_a_texture_is_drawn_even_when_suboptimal()
    {
        var swapChain = new SwapChainRecovery();
        Assert.Equal(SwapChainStep.Draw, swapChain.Next(Optimal, texture: true));
        Assert.Equal(SwapChainStep.Draw, swapChain.Next(SurfaceGetCurrentTextureStatus.SuccessSuboptimal, texture: true));
        Assert.Equal(0, swapChain.Missed);
    }

    [Fact]
    public void A_swap_chain_without_textures_is_configured_then_made_again_then_given_up()
    {
        var swapChain = new SwapChainRecovery();
        var steps = Enumerable.Range(0, SwapChainRecovery.FailAfter).Select(_ => swapChain.Next(Outdated, texture: false)).ToList();
        // Configured again each frame, its surface made again every so many, and given up on at last.
        Assert.Equal(SwapChainStep.Configure, steps[0]);
        Assert.Equal(SwapChainStep.Recreate, steps[SwapChainRecovery.RecreateAfter - 1]);
        Assert.Equal(SwapChainStep.Recreate, steps[2 * SwapChainRecovery.RecreateAfter - 1]);
        Assert.Equal(SwapChainStep.Fail, steps[^1]);
        Assert.Equal(SwapChainRecovery.FailAfter - 3, steps.Count(step => step == SwapChainStep.Configure));
    }

    [Fact]
    public void A_frame_shown_again_starts_the_count_over()
    {
        var swapChain = new SwapChainRecovery();
        for (int i = 0; i < SwapChainRecovery.RecreateAfter - 1; i++)
            swapChain.Next(Outdated, texture: false);
        Assert.Equal(SwapChainStep.Draw, swapChain.Next(Optimal, texture: true));
        Assert.Equal(SwapChainStep.Configure, swapChain.Next(Outdated, texture: false));
        Assert.Equal(1, swapChain.Missed);
    }

    [Fact]
    public void A_timeout_waits_without_configuring_and_an_error_gives_up_at_once()
    {
        var swapChain = new SwapChainRecovery();
        Assert.Equal(SwapChainStep.Skip, swapChain.Next(Timeout, texture: false));
        // Success without a texture is no frame either.
        Assert.Equal(SwapChainStep.Configure, swapChain.Next(Optimal, texture: false));
        Assert.Equal(SwapChainStep.Fail, new SwapChainRecovery().Next(SurfaceGetCurrentTextureStatus.Error, texture: false));
    }

    [Theory]
    [InlineData("WebGPU device lost (Unknown): D3D12 device removed", true)]
    [InlineData("[Device] is lost.", true)]
    [InlineData(GpuDevice.LostMessage, true)]
    [InlineData("WebGPU Validation: Binding size is zero.", false)]
    public void A_lost_device_is_known_however_it_is_met(string message, bool lost) =>
        Assert.Equal(lost, GpuDevice.IsLost(new InvalidOperationException(message)));

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? name);

    [Fact]
    public void A_window_whose_surface_is_made_again_keeps_showing_frames()
    {
        GpuDevice gpu = TestGpu.Device();
        // The other tests' GPU work waits: the device is used from one thread at a time, as the app's UI thread does.
        lock (gpu.Gate)
            Draw();
    }

    private static void Draw()
    {
        // A pop-up never shown nor activated, off the desktop: nothing on screen changes.
        const uint Popup = 0x80000000, NoActivate = 0x08000000, ToolWindow = 0x00000080;
        nint module = GetModuleHandle(null);
        nint window = CreateWindowEx(NoActivate | ToolWindow, "STATIC", "viewport", Popup, -4000, -4000, 320, 240, 0, 0, module, 0);
        Assert.NotEqual(0, window);
        try
        {
            var renderer = new ViewportRenderer(window, module, 320, 240);
            try
            {
                renderer.Show((ViewportMesh?)null);
                Assert.True(renderer.Draw());
                // The window takes one swap chain: the old surface lets go of it before the new one's is made.
                for (int i = 0; i < 3; i++)
                {
                    renderer.RecreateSurface();
                    Assert.True(renderer.Draw());
                    Assert.True(renderer.Draw());
                }
                Assert.Equal(0, renderer.MissedFrames);
            }
            finally
            {
                renderer.Dispose();
            }
        }
        finally
        {
            DestroyWindow(window);
        }
    }

    [Fact]
    public void A_second_surface_for_a_window_leaves_the_device_and_the_first_one_drawing()
    {
        GpuDevice gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            const uint Popup = 0x80000000, NoActivate = 0x08000000, ToolWindow = 0x00000080;
            nint module = GetModuleHandle(null);
            nint window = CreateWindowEx(NoActivate | ToolWindow, "STATIC", "viewport", Popup, -4000, -4000, 320, 240, 0, 0, module, 0);
            nint other = CreateWindowEx(NoActivate | ToolWindow, "STATIC", "other", Popup, -4000, -4000, 320, 240, 0, 0, module, 0);
            try
            {
                var renderer = new ViewportRenderer(window, module, 320, 240);
                try
                {
                    renderer.Show((ViewportMesh?)null);
                    Assert.True(renderer.Draw());
                    // What lost Dawn's device for good (the app drew with Dawn before): a second surface for the window,
                    // configured. wgpu refuses its swap chain with a validation error, and nothing else changes.
                    gpu.Device.PushErrorScope(ErrorFilter.Validation);
                    Surface second = gpu.Instance.CreateSurface(window, module)!;
                    second.Configure(new SurfaceConfiguration { Device = gpu.Device, Format = TextureFormat.BGRA8Unorm, Width = 320, Height = 240 });
                    gpu.Device.PopErrorScopeSync();
                    second.Dispose();
                    for (int i = 0; i < 3; i++)
                        Assert.True(renderer.Draw());
                    // And another window still gets a swap chain.
                    var fresh = new ViewportRenderer(other, module, 320, 240);
                    fresh.Show((ViewportMesh?)null);
                    Assert.True(fresh.Draw());
                    fresh.Dispose();
                    Assert.True(gpu.Device.Poll());
                }
                finally
                {
                    renderer.Dispose();
                }
            }
            finally
            {
                DestroyWindow(window);
                DestroyWindow(other);
            }
        }
    }
}
