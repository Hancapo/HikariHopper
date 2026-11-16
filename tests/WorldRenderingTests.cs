using System.Numerics;
using HikariHopper.Rendering;

namespace HikariHopper.Tests;

// The world's GPU side, without a game: every game's pipelines and a frame of it, with its shadow, raise no GPU errors.
public sealed class WorldRenderingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_worlds_pipelines_compile_and_its_frames_raise_no_GPU_errors(bool direct)
    {
        ViewportRenderer.DirectWorldDraws = direct;
        GpuDevice gpu = GpuDevice.Shared;
        lock (gpu.Gate)
        {
            var renderer = new ViewportRenderer(gpu);
            WebGpuSharp.Texture target = gpu.Device.CreateTexture(new WebGpuSharp.TextureDescriptor
            {
                Usage = WebGpuSharp.TextureUsage.RenderAttachment,
                Dimension = WebGpuSharp.TextureDimension.D2,
                Size = new WebGpuSharp.Extent3D(64, 32, 1),
                Format = renderer.PresentFormat,
                MipLevelCount = 1,
                SampleCount = 1,
            });
            gpu.Device.PushErrorScope(WebGpuSharp.ErrorFilter.Validation);
            renderer.CreateWorldPipelines();
            renderer.CreatePickPipelines();
            // Each game's: RDR2's material, and GTA V's surfaces and terrain, drawn, cast and picked.
            foreach (WorldFamily family in Enum.GetValues<WorldFamily>())
            {
                renderer.CreateWorldFamily(family);
                renderer.CreatePickFamily(family);
            }
            renderer.Resize(64, 32);
            renderer.WorldShown = true;
            renderer.Shadows = true;
            renderer.Camera.Frame(new Vector3(-300, 740, 132), 20);
            renderer.Camera.Free = true;
            renderer.Camera.Place(new Vector3(-300, 740, 132), -MathF.PI / 2, .18f);
            renderer.SetTime(14);
            renderer.RenderTo(target.CreateView());
            // Nothing streamed: nothing under the pointer.
            Assert.Null(renderer.PickWorldAt(32, 16));
            var (type, message) = gpu.Device.PopErrorScopeSync(5_000_000_000);
            Assert.True(type == WebGpuSharp.ErrorType.NoError, message);
            Assert.Equal(WorldShadowCount, renderer.CascadesCast);
            renderer.Dispose();
            target.Destroy();
        }
        ViewportRenderer.DirectWorldDraws = false;
    }

    private const long WorldShadowCount = Rendering.World.WorldShadowCascades.Count;
}
