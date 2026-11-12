using System.Numerics;
using HikariHopper.Rendering;

namespace HikariHopper.Tests;

public sealed class ExposureTests
{
    // A bright emissive square standing on the ground, 2 m wide.
    private static DrawableScene Sign()
    {
        var mesh = new FiveFury.Drawables.DrawableMeshInput([new(-1, 0, 0), new(1, 0, 0), new(1, 0, 2), new(-1, 0, 2)], [0, 1, 2, 0, 2, 3], "glow");
        var builder = new FiveFury.Drawables.DrawableBuilder { Name = "sign" };
        builder.Materials.Add(new FiveFury.Drawables.DrawableMaterialInput { Name = "glow", Shader = "emissive", Parameters = { ["emissiveMultiplier"] = [40f] } });
        var model = new FiveFury.Drawables.DrawableModelInput();
        model.Meshes.Add(mesh);
        builder.Models.Add(model);
        byte[] ydr = new FiveFury.Ydr.Ydr(builder.Build(FiveFury.GameTarget.Legacy), FiveFury.GameTarget.Legacy).Write();
        return ModelContent.Read("sign.ydr", ydr).Surfaces!;
    }

    [Fact]
    public void How_close_the_camera_is_doesnt_change_the_exposure()
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
        // Rows of a texture copy are a multiple of 256 bytes.
        const int Size = 128;
        DrawableScene scene = Sign();
        byte[] far, near;
        float farMean, nearMean;
        lock (gpu.Gate)
        {
            var renderer = new ViewportRenderer(gpu);
            renderer.Resize(Size, Size);
            renderer.Show(scene);
            renderer.Frame(scene.Box);
            renderer.SetTime(23);
            renderer.Antialiasing = false;
            WebGpuSharp.Texture target = gpu.Device.CreateTexture(new WebGpuSharp.TextureDescriptor
            {
                Usage = WebGpuSharp.TextureUsage.RenderAttachment | WebGpuSharp.TextureUsage.CopySrc,
                Dimension = WebGpuSharp.TextureDimension.D2,
                Size = new WebGpuSharp.Extent3D(Size, Size, 1),
                Format = renderer.PresentFormat,
                MipLevelCount = 1,
                SampleCount = 1,
            });
            WebGpuSharp.TextureView view = target.CreateView();
            byte[] Draw()
            {
                renderer.RenderTo(view);
                WebGpuSharp.Buffer readback = gpu.Device.CreateBuffer(new WebGpuSharp.BufferDescriptor
                {
                    Size = Size * Size * 4,
                    Usage = WebGpuSharp.BufferUsage.CopyDst | WebGpuSharp.BufferUsage.MapRead,
                });
                WebGpuSharp.CommandEncoder encoder = gpu.Device.CreateCommandEncoder();
                encoder.CopyTextureToBuffer(new WebGpuSharp.TexelCopyTextureInfo { Texture = target },
                    new WebGpuSharp.TexelCopyBufferInfo { Buffer = readback, Layout = new WebGpuSharp.TexelCopyBufferLayout { BytesPerRow = Size * 4, RowsPerImage = Size } },
                    new WebGpuSharp.Extent3D(Size, Size, 1));
                gpu.Queue.Submit(encoder.Finish());
                readback.MapSync(WebGpuSharp.MapMode.Read);
                byte[] pixels = readback.GetConstMappedRange<byte[], byte>(0, Size * Size * 4, span => span.ToArray());
                readback.Unmap();
                readback.Destroy();
                return pixels;
            }
            // Far out, the sign a speck in the night; then close, filling the view: the same way, so the corner is
            // the same sky.
            renderer.Camera.Zoom(-12);
            far = Draw();
            farMean = renderer.MeasuredMean();
            renderer.Camera.Zoom(24);
            near = Draw();
            nearMean = renderer.MeasuredMean();
            target.Destroy();
            renderer.Dispose();
        }
        // What the exposure is worked out from is the same close up as far out: the exposure held.
        Assert.True(farMean > 0, $"{farMean}");
        Assert.Equal(farMean, nearMean);
        // And close up the sign does fill much more of the view, which a measure of the frame would have followed.
        int Bright(byte[] pixels) => Enumerable.Range(0, Size * Size).Count(i => pixels[i * 4 + 1] > 200);
        Assert.True(Bright(near) > 4 * Bright(far), $"{Bright(near)} bright pixels close up, {Bright(far)} far out");
    }
}
