using System.Numerics;
using System.Runtime.InteropServices;
using HikariHopper.Rendering;

namespace HikariHopper.Tests;

/// <summary>Red Dead Redemption 2's drawables drawn with RedFury's material (Rdr2/surface.wgsl), from the installed game.</summary>
public sealed class Rdr2MaterialTests
{
    // A prop carrying every texture it names: a pile of logs, standard shader, with a colour, bump and material A map.
    private static (string Name, byte[] Data) Prop() =>
        Rdr2ModelTests.Find(".ydr", (_, data) => RedFury.Ydr.Ydr.Read(data).Drawable is { EmbeddedTextures.Textures.Count: > 2 } drawable
            && !drawable.UnresolvedTextures().Any() && drawable.Materials.All(m => m.ShaderName == "standard"))
        ?? throw new InvalidOperationException("None of the archives searched holds a standard prop with its own textures");

    [Fact]
    public void Its_materials_are_RedFurys_with_their_maps_in_their_slots()
    {
        (string name, byte[] data) = Prop();
        DrawableScene scene = ModelContent.Read(name, data).Surfaces!;
        Assert.All(scene.Materials, material =>
        {
            Rdr2Material rdr2 = Assert.IsType<Rdr2Material>(material.Rdr2);
            Assert.True(material.Rebuilt);
            Assert.Empty(material.MissingTextures);
            // The colour map in slot 0, as the fallback's diffuse too.
            Assert.NotNull(rdr2.Textures[0]);
            Assert.Same(rdr2.Textures[0], material.Diffuse);
            float[] f = MemoryMarshal.Cast<byte, float>(rdr2.Uniforms.AsSpan()).ToArray();
            uint[] u = MemoryMarshal.Cast<byte, uint>(rdr2.Uniforms.AsSpan()).ToArray();
            // One layer, opaque, cut out at half, white base colour.
            Assert.Equal([1f, 1f, 1f, 1f], f[0..4]);
            Assert.Equal((.5f, 1f, 0f), (f[9], f[10], f[11]));
            // A bump map flags layer 0's normal; a material A map (SpecularTex) flags layer 0's material.
            Assert.Equal(rdr2.Textures[1] is not null ? 1u : 0u, u[16] & 1);
            Assert.Equal(rdr2.Textures[13] is not null ? 2u : 0u, u[17] & 2);
        });
    }

    [Fact]
    public void Its_vertices_carry_the_colour_and_tint_coordinate_RedFurys_program_reads()
    {
        (string name, byte[] data) = Prop();
        DrawableScene scene = ModelContent.Read(name, data).Surfaces!;
        // The tint palette's coordinate is the first colour's blue, in the second colour's red.
        Assert.All(scene.Vertices, vertex => Assert.Equal((vertex.Colour0 >> 16) & 0xFF, vertex.Colour1 & 0xFF));
    }

    [Fact]
    public void It_is_drawn_with_RedFurys_material_without_a_GPU_error()
    {
        (string name, byte[] data) = Prop();
        DrawableScene scene = ModelContent.Read(name, data).Surfaces!;
        GpuDevice gpu = TestGpu.Device();
        var errors = new List<string>();
        Action<string> listen = message =>
        {
            lock (errors)
                errors.Add(message);
        };
        gpu.Listen(listen);
        const int Size = 64;
        byte[] Draw(DrawableScene? drawn, out bool rdr2)
        {
            lock (gpu.Gate)
            {
                var renderer = new ViewportRenderer(gpu);
                renderer.Resize(Size, Size);
                if (drawn is not null)
                    renderer.Show(drawn);
                else
                    renderer.Show(new ViewportMesh(Vector3.Zero));
                renderer.Frame(scene.Box);
                renderer.SetTime(13);
                rdr2 = renderer.Rdr2Supported;
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
                target.Destroy();
                renderer.Dispose();
                return pixels;
            }
        }
        byte[] empty = Draw(null, out _);
        byte[] drawn = Draw(scene, out bool supported);
        GC.KeepAlive(listen);
        if (!supported)
            Assert.Skip("This GPU binds fewer textures than RDR2's material reads");
        lock (errors)
            Assert.Empty(errors);
        // The prop covers the middle of the view.
        int at = (Size / 2 * Size + Size / 2) * 4;
        Assert.NotEqual(empty[at..(at + 3)], drawn[at..(at + 3)]);
    }
}
