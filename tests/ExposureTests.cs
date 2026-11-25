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
            or HikariHopper.Gpu.GpuException)
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
            byte[] Draw()
            {
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
