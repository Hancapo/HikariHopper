using System.Numerics;
using CommonFury.Vertices;
using FiveFury;
using FiveFury.Drawables;
using FiveFury.Ydr;
using HikariHopper.Rendering;
using HikariHopper.Tests.Textures;

namespace HikariHopper.Tests;

public sealed class GlassShaderTests
{
    // A flat square facing up, of the given glass shader, with a grey half-clear diffuse map of its own.
    private static byte[] Pane(GameTarget target, string shader, Action<DrawableMaterialInput>? setup = null)
    {
        Vector3[] positions = [new(-1, -1, 0), new(1, -1, 0), new(1, 1, 0), new(-1, 1, 0)];
        var mesh = new DrawableMeshInput(positions, [0, 1, 2, 0, 2, 3], "glass");
        mesh.SetChannel(VertexSemantic.Normal, [.. Enumerable.Repeat(Vector4.UnitZ, 4)]);
        mesh.SetChannel(VertexSemantic.TexCoord0, [new(0, 0, 0, 0), new(1, 0, 0, 0), new(1, 1, 0, 0), new(0, 1, 0, 0)]);
        mesh.SetChannel(VertexSemantic.Color0, [.. Enumerable.Repeat(Vector4.One, 4)]);
        var material = new DrawableMaterialInput { Name = "glass", Shader = shader };
        material.Textures["DiffuseSampler"] = new FiveFury.Textures.TextureReference("window");
        setup?.Invoke(material);
        var builder = new DrawableBuilder { Name = "car" };
        builder.Materials.Add(material);
        builder.AdditionalTextures.Add(TextureTestKit.Bgra("window", 4, 4, [64, 64, 64, 128]));
        var model = new DrawableModelInput();
        model.Meshes.Add(mesh);
        builder.Models.Add(model);
        return new Ydr(builder.Build(target), target).Write();
    }

    private static SurfaceMaterial Read(byte[] ydr) => Assert.Single(ModelContent.Read("car.ydr", ydr).Surfaces!.Materials);

    [Theory]
    [InlineData(GameTarget.Legacy, "vehicle_vehglass", 1)]
    [InlineData(GameTarget.Legacy, "vehicle_vehglass_crack", 2)]
    [InlineData(GameTarget.Legacy, "vehicle_vehglass_inner", 3)]
    [InlineData(GameTarget.Enhanced, "vehicle_vehglass", 1)]
    [InlineData(GameTarget.Enhanced, "vehicle_vehglass_inner", 3)]
    public void A_vehicles_glass_is_drawn_with_its_own_program_blended(GameTarget target, string shader, int program)
    {
        SurfaceMaterial material = Read(Pane(target, shader, m =>
        {
            m.Parameters["matDiffuseColorTint"] = [.5f, .25f, .75f, .4f];
            m.Parameters["dirtLevelMod"] = [1, .5f, .3f, 1];
        }));
        Assert.True(material.Glass);
        Assert.True(material.Rebuilt);
        Assert.False(material.Vehicle);
        Assert.Equal(AlphaMode.Blend, material.AlphaMode);
        float[] u = material.Uniforms;
        Assert.Equal(program, u[92]);
        // matDiffuseColorTint, which the vertex alpha blends the diffuse towards.
        Assert.Equal([.5f, .25f, .75f, .4f], u[96..100]);
        // matDiffuseColor, unset: white. dirtLevelMod as the material has it, but clean, as the viewer shows a vehicle.
        Assert.Equal([1f, 1f, 1f], u[16..19]);
        Assert.Equal([0f, .5f, .3f, 1f], u[20..24]);
    }

    [Theory]
    [InlineData(GameTarget.Legacy, "glass", 4, 0, 0)]
    [InlineData(GameTarget.Legacy, "glass_spec", 4, 0, 0)]
    [InlineData(GameTarget.Legacy, "glass_env", 4, 1, 3)]
    [InlineData(GameTarget.Legacy, "glass_emissivenight", 4, 0, 0)]
    [InlineData(GameTarget.Legacy, "glass_breakable", 5, 1, 3)]
    [InlineData(GameTarget.Legacy, "glass_breakable_crack", 5, 1, 3)]
    [InlineData(GameTarget.Enhanced, "glass_env", 4, 1, 3)]
    [InlineData(GameTarget.Enhanced, "glass_breakable", 5, 1, 3)]
    public void The_maps_glass_is_drawn_with_its_own_program_blended(GameTarget target, string shader, int program, int glassLit,
        int reflection)
    {
        SurfaceMaterial material = Read(Pane(target, shader, m =>
        {
            m.Parameters["specularFresnel"] = [.9f];
            m.Parameters["specularIntensityMult"] = [.5f];
        }));
        Assert.True(material.MapGlass);
        Assert.False(material.Glass);
        Assert.True(material.Rebuilt);
        Assert.Equal(AlphaMode.Blend, material.AlphaMode);
        float[] u = material.Uniforms;
        Assert.Equal(program, u[92]);
        Assert.Equal(glassLit, u[93]);
        Assert.Equal(0, u[94]);
        // The reflection map for glass_env and the breakable glass; a sphere map only where the material has one.
        Assert.Equal(reflection, u[100]);
        Assert.Equal([.9f, 100f, .5f], u[0..3]);
        Assert.Equal(shader == "glass_emissivenight" ? 1 : 0, u[102]);
    }

    [Fact]
    public void Glass_displacement_is_lit_white_by_the_vertex_alpha()
    {
        SurfaceMaterial material = Read(Pane(GameTarget.Legacy, "glass_displacement"));
        Assert.True(material.MapGlass);
        Assert.Equal(1, material.Uniforms[94]);
    }

    // The pane drawn at the centre of a small view, from above (its front) or below (its back); the centre's colour.
    private static (byte R, byte G, byte B) Centre(byte[]? ydr, bool above)
    {
        GpuDevice gpu = TestGpu.Device();
        const int Size = 64;
        lock (gpu.Gate)
        {
            var renderer = new ViewportRenderer(gpu);
            renderer.Resize(Size, Size);
            var box = new CommonFury.Numerics.Aabb3(new Vector3(-1, -1, -.01f), new Vector3(1, 1, .01f));
            if (ydr is not null)
                renderer.Show(ModelContent.Read("car.ydr", ydr).Surfaces!);
            else
                renderer.Show(new ViewportMesh(Vector3.Zero));
            renderer.Frame(box);
            renderer.Camera.Orbit(0, above ? 1000 : -1000);
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
            int at = (Size / 2 * Size + Size / 2) * 4;
            return (pixels[at], pixels[at + 1], pixels[at + 2]);
        }
    }

    [Theory]
    [InlineData("vehicle_vehglass")]
    [InlineData("glass_env")]
    [InlineData("glass")]
    public void Glass_is_seen_from_its_front_and_culled_from_its_back_as_the_game_draws_it(string shader)
    {
        byte[] pane = Pane(GameTarget.Legacy, shader);
        // From below, its back: culled, so what is behind it shows as if it weren't there.
        Assert.Equal(Centre(null, above: false), Centre(pane, above: false));
        // From above, its front: drawn over what is behind it.
        Assert.NotEqual(Centre(null, above: true), Centre(pane, above: true));
    }
}
