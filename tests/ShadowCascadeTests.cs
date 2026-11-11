using System.Numerics;
using HikariHopper.Rendering;

namespace HikariHopper.Tests;

public sealed class ShadowCascadeTests
{
    // A flat square of ground 200 m across, a big map piece's size.
    private static DrawableScene Ground()
    {
        var mesh = new FiveFury.Drawables.DrawableMeshInput([new(-100, -100, 0), new(100, -100, 0), new(100, 100, 0), new(-100, 100, 0)], [0, 1, 2, 0, 2, 3], "ground");
        var builder = new FiveFury.Drawables.DrawableBuilder { Name = "ground" };
        builder.Materials.Add(new FiveFury.Drawables.DrawableMaterialInput { Name = "ground", Shader = "default" });
        var model = new FiveFury.Drawables.DrawableModelInput();
        model.Meshes.Add(mesh);
        builder.Models.Add(model);
        byte[] ydr = new FiveFury.Ydr.Ydr(builder.Build(FiveFury.GameTarget.Legacy), FiveFury.GameTarget.Legacy).Write();
        return ModelContent.Read("ground.ydr", ydr).Surfaces!;
    }

    [Fact]
    public void Close_up_the_nearest_cascade_is_far_finer_than_one_map_over_the_whole_model()
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
        DrawableScene scene = Ground();
        float radius = scene.Box.Size.Length() / 2;
        lock (gpu.Gate)
        {
            gpu.Device.PushErrorScope(WebGpuSharp.ErrorFilter.Validation);
            var renderer = new ViewportRenderer(gpu);
            renderer.Resize(256, 128);
            renderer.Show(scene);
            renderer.Frame(scene.Box);
            renderer.SetTime(17);
            WebGpuSharp.Texture target = gpu.Device.CreateTexture(new WebGpuSharp.TextureDescriptor
            {
                Usage = WebGpuSharp.TextureUsage.RenderAttachment,
                Dimension = WebGpuSharp.TextureDimension.D2,
                Size = new WebGpuSharp.Extent3D(256, 128, 1),
                Format = renderer.PresentFormat,
                MipLevelCount = 1,
                SampleCount = 1,
            });
            // Close to the ground, as when looking at a rock on a map piece.
            renderer.Camera.Zoom(20);
            renderer.RenderTo(target.CreateView());
            Vector4 texels = renderer.ShadowTexels, splits = renderer.ShadowSplits;
            target.Destroy();
            renderer.Dispose();
            (WebGpuSharp.ErrorType Type, string Message) result = gpu.Device.PopErrorScopeSync(10_000_000_000);
            Assert.True(result.Type == WebGpuSharp.ErrorType.NoError, $"{result.Type}: {result.Message}");

            // One map over the whole model would have a texel of its diameter over the resolution: about 14 cm here.
            float whole = radius * 2 / ViewportRenderer.ShadowResolution;
            Assert.True(texels.X < whole / 10, $"nearest cascade's texel {texels.X} m, one map's {whole} m");
            // Each cascade reaches further and is coarser than the one before; the last takes in the far side.
            Assert.True(splits.X < splits.Y && splits.Y < splits.Z && splits.Z < splits.W, $"{splits}");
            Assert.True(texels.X <= texels.Y && texels.Y <= texels.Z && texels.Z <= texels.W, $"{texels}");
            Assert.True(splits.W >= (renderer.Camera.Eye - scene.Origin).Length() + radius - .01f);
        }
    }
}
