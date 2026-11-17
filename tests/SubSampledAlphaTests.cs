using System.Numerics;
using CommonFury.Vertices;
using FiveFury;
using FiveFury.Drawables;
using FiveFury.Ydr;
using HikariHopper.Rendering;
using HikariHopper.Tests.Textures;

namespace HikariHopper.Tests;

// GTA V's sub-sampled alpha: its cut-outs' references and HardAlphaBlend, and the combine that blends the checkerboard
// they're drawn on.
public sealed class SubSampledAlphaTests
{
    // A cut-out (the cut-out bucket) two metres square, standing up and facing -Y, its middle at (0, 0, 1), its diffuse
    // map one grey of this alpha.
    private static byte[] CutOut(string shader, byte alpha, Action<DrawableMaterialInput>? setup = null)
    {
        Vector3[] positions = [new(-1, 0, 0), new(1, 0, 0), new(1, 0, 2), new(-1, 0, 2)];
        var mesh = new DrawableMeshInput(positions, [0, 1, 2, 0, 2, 3], "fence");
        mesh.SetChannel(VertexSemantic.Normal, [.. Enumerable.Repeat(-Vector4.UnitY, 4)]);
        mesh.SetChannel(VertexSemantic.TexCoord0, [new(0, 1, 0, 0), new(1, 1, 0, 0), new(1, 0, 0, 0), new(0, 0, 0, 0)]);
        mesh.SetChannel(VertexSemantic.Color0, [.. Enumerable.Repeat(Vector4.One, 4)]);
        var builder = new DrawableBuilder { Name = "fence" };
        builder.AdditionalTextures.Add(TextureTestKit.Bgra("fence", 4, 4, [230, 230, 230, alpha]));
        var material = new DrawableMaterialInput { Name = "fence", Shader = shader, RenderBucket = 3 };
        material.Textures["DiffuseSampler"] = new FiveFury.Textures.TextureReference("fence");
        setup?.Invoke(material);
        builder.Materials.Add(material);
        var model = new DrawableModelInput();
        model.Meshes.Add(mesh);
        builder.Models.Add(model);
        return new Ydr(builder.Build(GameTarget.Legacy), GameTarget.Legacy).Write();
    }

    private static SurfaceMaterial Material(byte[] ydr) => ModelContent.Read("fence.ydr", ydr).Surfaces!.Materials.Single();

    [Fact]
    public void Cut_outs_take_the_game_s_alpha_references_and_their_own_hard_alpha_blend()
    {
        SurfaceMaterial fence = Material(CutOut("normal_spec", 255));
        Assert.Equal(AlphaMode.Mask, fence.AlphaMode);
        // global_alphaRef, as the game's code sets it, and HardAlphaBlend's default, whole.
        Assert.Equal(90 / 255f, fence.Uniforms[12], 6);
        Assert.Equal(1, fence.Uniforms[74]);
        // A material's own HardAlphaBlend.
        Assert.Equal(.25f, Material(CutOut("normal_spec", 255, m => m.Parameters["HardAlphaBlend"] = [.25f])).Uniforms[74]);
        // The vehicles' own reference, all but nought.
        Assert.Equal(1 / 255f, Material(CutOut("vehicle_mesh", 255)).Uniforms[12], 6);
    }

    // The view of the cut-out, without anti-aliasing, as the window's bytes, with the frames' GPU error, if any.
    private const int Size = 64;

    private static byte[] Render(byte[]? ydr, bool combine, out string? error)
    {
        GpuDevice gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            var renderer = new ViewportRenderer(gpu) { Antialiasing = false, Shadows = false, SubSampledCombine = combine };
            renderer.Resize(Size, Size);
            gpu.Device.PushErrorScope(WebGpuSharp.ErrorFilter.Validation);
            if (ydr is not null)
                renderer.Show(ModelContent.Read("fence.ydr", ydr).Surfaces!);
            else
                renderer.Show(new ViewportMesh(Vector3.Zero));
            renderer.Frame(new CommonFury.Numerics.Aabb3(new Vector3(-1, -1, 0), new Vector3(1, 1, 2)));
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
            for (int i = 0; i < 4; i++)
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
            var (type, message) = gpu.Device.PopErrorScopeSync(5_000_000_000);
            error = type == WebGpuSharp.ErrorType.NoError ? null : $"{type}: {message}";
            target.Destroy();
            renderer.Dispose();
            return pixels;
        }
    }

    // Over the middle of the view: the mean green, and how far a pixel's green is from the next one's on its row.
    private static (double Level, double Step) Middle(byte[] pixels)
    {
        double level = 0, step = 0;
        int count = 0;
        for (int y = Size / 2 - 8; y < Size / 2 + 8; y++)
            for (int x = Size / 2 - 8; x < Size / 2 + 8; x++)
            {
                int at = (y * Size + x) * 4 + 1;
                level += pixels[at];
                step += Math.Abs(pixels[at] - pixels[at + 4]);
                count++;
            }
        return (level / count, step / count);
    }

    [Fact]
    public void The_combine_blends_a_part_covering_cut_out_s_checkerboard_with_what_is_behind_it()
    {
        // Alpha 100 of 255: past the reference, under the hard ramp's top, so drawn on half the pixels.
        byte[] fence = CutOut("normal_spec", 100);
        (double Level, double Step) empty = Middle(Render(null, combine: true, out _));
        (double Level, double Step) dithered = Middle(Render(fence, combine: false, out string? error));
        Assert.Null(error);
        (double Level, double Step) blended = Middle(Render(fence, combine: true, out error));
        Assert.Null(error);
        // Left alone, a checkerboard of the grey and the backdrop; combined, the two blended evenly, between them.
        Assert.True(dithered.Step > 20, $"dithered {dithered}");
        Assert.True(blended.Step < 4, $"blended {blended}");
        Assert.True(blended.Level > empty.Level + 10, $"blended {blended}, empty {empty}");
    }

    [Fact]
    public void Whole_and_all_but_empty_cut_outs_are_left_as_they_are_drawn()
    {
        // Whole: drawn on every pixel, which the combine leaves alone within it.
        byte[] whole = CutOut("normal_spec", 255);
        Assert.Equal(Middle(Render(whole, combine: false, out _)), Middle(Render(whole, combine: true, out string? error)));
        Assert.Null(error);
        // Under the reference: nothing of it drawn, the backdrop as it is without it (but for its exposure's settling).
        (double Level, double Step) empty = Middle(Render(null, combine: true, out _));
        (double Level, double Step) under = Middle(Render(CutOut("normal_spec", 80), combine: true, out error));
        Assert.Null(error);
        Assert.Equal(empty.Level, under.Level, .5);
        Assert.Equal(empty.Step, under.Step, .5);
    }
}
