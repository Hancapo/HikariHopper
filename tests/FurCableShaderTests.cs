using System.Numerics;
using CommonFury.Vertices;
using FiveFury;
using FiveFury.Drawables;
using FiveFury.Textures;
using FiveFury.Ydr;
using HikariHopper.Rendering;
using GameFormat = CommonFury.Textures.TextureFormat;

namespace HikariHopper.Tests;

public sealed class FurCableShaderTests
{
    private static DrawableScene Read(GameTarget target, string shader, Action<DrawableMaterialInput>? setup = null, DrawableMeshInput? mesh = null)
    {
        DrawableMeshInput input = mesh ?? Quad();
        var material = new DrawableMaterialInput { Name = "surface", Shader = shader };
        setup?.Invoke(material);
        var builder = new DrawableBuilder { Name = "prop" };
        builder.Materials.Add(material);
        var model = new DrawableModelInput();
        model.Meshes.Add(input);
        builder.Models.Add(model);
        byte[] ydr = new Ydr(builder.Build(target), target).Write();
        return ModelContent.Read("prop.ydr", ydr).Surfaces!;
    }

    // A one-metre square, flat, facing up.
    private static DrawableMeshInput Quad()
    {
        Vector3[] positions = [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0)];
        var mesh = new DrawableMeshInput(positions, [0, 1, 2, 0, 2, 3], "surface");
        mesh.SetChannel(VertexSemantic.Normal, [.. Enumerable.Repeat(Vector4.UnitZ, 4)]);
        mesh.SetChannel(VertexSemantic.TexCoord0, [new(0, 0, 0, 0), new(1, 0, 0, 0), new(1, 1, 0, 0), new(0, 1, 0, 0)]);
        mesh.SetChannel(VertexSemantic.Color0, [.. Enumerable.Repeat(Vector4.One, 4)]);
        return mesh;
    }

    // A wire from (0, 0, 1) to (4, 0, 1): each point twice, one either side, the normal along it, the radius signed by
    // the side in the first UV's x.
    private static DrawableMeshInput Wire(float radius)
    {
        Vector3[] positions = [new(0, 0, 1), new(0, 0, 1), new(4, 0, 1), new(4, 0, 1)];
        var mesh = new DrawableMeshInput(positions, [0, 1, 2, 2, 1, 3], "surface");
        mesh.SetChannel(VertexSemantic.Normal, [.. Enumerable.Repeat(Vector4.UnitX, 4)]);
        mesh.SetChannel(VertexSemantic.TexCoord0, [new(radius, 0, 0, 0), new(-radius, 0, 0, 0), new(radius, 0, 0, 0), new(-radius, 0, 0, 0)]);
        mesh.SetChannel(VertexSemantic.Color0, [.. Enumerable.Repeat(new Vector4(.2f, 0, .2f, 1), 4)]);
        return mesh;
    }

    private static SurfaceMaterial Material(GameTarget target, string shader, Action<DrawableMaterialInput>? setup = null) =>
        Assert.Single(Read(target, shader, setup).Materials);

    private static Texture Map(string name) => new(name, 4, 4, GameFormat.BC1Unorm, 1, new byte[8]);

    private static void FurMaps(DrawableMaterialInput m)
    {
        m.Textures["DiffuseSampler"] = new TextureReference(Map("im_grass07b_fur"));
        m.Textures["BumpSampler"] = new TextureReference(Map("fur_grass_n_fade"));
        m.Textures["SpecSampler"] = new TextureReference(Map("im_grass07b_fur_s"));
        for (int i = 0; i < 4; i++)
            m.Textures[$"ComboHeightSamplerFur{2 * i}{2 * i + 1}"] = new TextureReference(Map($"fur_grass_rgba4_{i}"));
    }

    [Fact]
    public void Fur_grass_reads_the_same_from_either_edition()
    {
        SurfaceMaterial Fur(GameTarget target) => Material(target, "grass_fur", m =>
        {
            m.Parameters["specularFresnel"] = [.99f];
            m.Parameters["specularFalloffMult"] = [8];
            m.Parameters["specularIntensityMult"] = [.25f];
            m.Parameters["bumpiness"] = [1.5f];
            m.Parameters["furLayerParams"] = [.005f, .001f, .001f, .7f];
            m.Parameters["furUvScales"] = [1, 1.2f, 3, 1];
            m.Parameters["furAlphaClip03"] = [0, .039f, .041f, .081f];
            FurMaps(m);
        });
        SurfaceMaterial legacy = Fur(GameTarget.Legacy), enhanced = Fur(GameTarget.Enhanced);
        foreach (SurfaceMaterial fur in new[] { legacy, enhanced })
        {
            Assert.True(fur.Fur);
            Assert.True(fur.Rebuilt);
            Assert.False(fur.Cable || fur.Terrain || fur.Ped || fur.Vehicle);
            Assert.All(fur.FurHeights, Assert.NotNull);
            Assert.Equal(["fur_grass_rgba4_0", "fur_grass_rgba4_1", "fur_grass_rgba4_2", "fur_grass_rgba4_3"], fur.FurHeights.Select(t => t!.Name));
            Assert.Empty(fur.MissingTextures);
            float[] u = fur.Uniforms;
            Assert.Equal([.99f, 8, .25f, 1.5f], u[..4]);
            Assert.Equal([1f, 0, 0], u[4..7]);
            Assert.Equal([.005f, .001f, .001f, .7f], u[8..12]);
            Assert.Equal([1, 1.2f, 3, 1], u[12..16]);
            // Plain grass_fur, its own clips for the first four shells, the game's for the rest, and the game's shades.
            Assert.Equal(0, u[16]);
            Assert.Equal([0, .039f, .041f, .081f], u[20..24]);
            Assert.Equal([20 / 255f, 25 / 255f, 30 / 255f, 34 / 255f], u[24..28], new Near());
            Assert.Equal([90 / 255f, 118 / 255f, 148 / 255f, 168 / 255f, 200 / 255f, 220 / 255f, 238 / 255f, 1], u[28..36], new Near());
        }
        Assert.Equal(legacy.Uniforms, enhanced.Uniforms);
    }

    [Fact]
    public void Masked_fur_takes_its_mask_and_colour_maps_and_tinted_fur_its_palette()
    {
        SurfaceMaterial masked = Material(GameTarget.Legacy, "grass_fur_mask", m =>
        {
            FurMaps(m);
            m.Textures["FurMaskSampler"] = new TextureReference(Map("dt1_13_fur_grass_mask"));
            m.Textures["DiffuseHfSampler"] = new TextureReference(Map("dt1_13_fur_grass_diffuse"));
        });
        Assert.True(masked.Fur);
        Assert.Equal("dt1_13_fur_grass_mask", masked.FurMask?.Name);
        Assert.Equal("dt1_13_fur_grass_diffuse", masked.DiffuseHf?.Name);
        Assert.Equal(1, masked.Uniforms[16]);
        // The game's defaults: a step of 0.008 m between shells, and no fur shaded at full brightness.
        Assert.Equal([.008f, 0, 0, 1], masked.Uniforms[8..12], new Near());

        SurfaceMaterial tinted = Material(GameTarget.Legacy, "grass_fur_tnt", m =>
        {
            FurMaps(m);
            m.Textures["TintPaletteSampler"] = new TextureReference(Map("fur_palette"));
            m.Parameters["tintPaletteSelector"] = [.5f, 0];
        });
        Assert.True(tinted.Fur);
        Assert.Equal(2, tinted.Uniforms[16]);
        Assert.Equal(.5f, tinted.Uniforms[17]);
    }

    [Fact]
    public void A_cable_takes_its_colours_and_blends_from_either_edition()
    {
        SurfaceMaterial Cable(GameTarget target) => Material(target, "cable", m =>
        {
            m.Parameters["shader_radiusScale"] = [2.5f];
            m.Parameters["shader_cableAmbient"] = [.4f, .4f];
            m.Parameters["shader_cableDiffuse2"] = [.1f, .2f, .3f];
        });
        SurfaceMaterial legacy = Cable(GameTarget.Legacy), enhanced = Cable(GameTarget.Enhanced);
        foreach (SurfaceMaterial cable in new[] { legacy, enhanced })
        {
            Assert.True(cable.Cable);
            Assert.True(cable.Rebuilt);
            Assert.False(cable.Fur);
            float[] u = cable.Uniforms;
            // radiusScale, fadeExponent's default, no emissive, the game's alpha test; black, then the second colour.
            Assert.Equal([2.5f, 1, 0, .1f], u[..4]);
            Assert.Equal([0f, 0, 0], u[4..7]);
            Assert.Equal([.1f, .2f, .3f], u[8..11]);
            Assert.Equal([.4f, .4f], u[12..14]);
        }
        Assert.Equal(legacy.Uniforms, enhanced.Uniforms);
        Assert.DoesNotContain(Read(GameTarget.Legacy, "cable").Diagnostics, d => d.StartsWith("Not rebuilt", StringComparison.Ordinal));
    }

    [Fact]
    public void Fur_grows_its_shells_off_the_surface_and_a_cable_is_drawn_wide_enough_to_see()
    {
        GpuDevice gpu;
        try
        {
            gpu = TestGpu.Device();
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException or DllNotFoundException
            or WebGpuSharp.WebGPUException)
        {
            Assert.Skip($"No GPU to test on: {error.Message}");
            return;
        }
        // Seen edge on, a flat square is all but nothing; with fur, its shells stand over it. The combo height maps
        // read black and opaque: the odd shells, which read their alpha, stand.
        DrawableScene bare = Read(GameTarget.Legacy, "default");
        DrawableScene fur = Read(GameTarget.Legacy, "grass_fur", m =>
        {
            FurMaps(m);
            m.Parameters["furLayerParams"] = [.05f, 0, 0, 1];
        });
        int Covered(DrawableScene? scene, Vector3 from, CommonFury.Numerics.Aabb3 box) => Drawn(gpu, scene, from, box);
        var square = new CommonFury.Numerics.Aabb3(new Vector3(0, 0, 0), new Vector3(1, 1, .35f));
        int bareCover = Covered(bare, -Vector3.UnitY, square), furCover = Covered(fur, -Vector3.UnitY, square);
        Assert.True(furCover > bareCover + 500, $"{furCover} pixels with fur, {bareCover} without");

        // A wire 2 cm thick, 4 m long: a ribbon of no width until the cable shader widens it to face the camera.
        DrawableScene wire = Read(GameTarget.Legacy, "cable", m =>
        {
            m.Parameters["shader_cableDiffuse"] = [1, 1, 1];
            m.Parameters["shader_cableAmbient"] = [1, 0];
        }, Wire(.02f));
        var span = new CommonFury.Numerics.Aabb3(new Vector3(0, -.5f, .5f), new Vector3(4, .5f, 1.5f));
        int wireCover = Covered(wire, Vector3.UnitZ, span);
        Assert.True(wireCover > 100, $"{wireCover} pixels of wire");
    }

    // How many pixels a scene changes over the empty backdrop, seen from an axis.
    private static int Drawn(GpuDevice gpu, DrawableScene? scene, Vector3 from, CommonFury.Numerics.Aabb3 box)
    {
        const int Size = 128;
        byte[] empty, drawn;
        (WebGpuSharp.ErrorType Type, string Message) result;
        lock (gpu.Gate)
        {
            gpu.Device.PushErrorScope(WebGpuSharp.ErrorFilter.Validation);
            var renderer = new ViewportRenderer(gpu);
            renderer.Resize(Size, Size);
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
            byte[] Draw(DrawableScene? shown)
            {
                renderer.Show(shown);
                renderer.Frame(box);
                renderer.Camera.LookFrom(from);
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
            empty = Draw(null);
            drawn = Draw(scene);
            target.Destroy();
            renderer.Dispose();
            result = gpu.Device.PopErrorScopeSync(10_000_000_000);
        }
        Assert.True(result.Type == WebGpuSharp.ErrorType.NoError, $"{result.Type}: {result.Message}");
        int changed = 0;
        for (int at = 0; at < empty.Length; at += 4)
            if (Math.Abs(empty[at] - drawn[at]) + Math.Abs(empty[at + 1] - drawn[at + 1]) + Math.Abs(empty[at + 2] - drawn[at + 2]) > 6)
                changed++;
        return changed;
    }

    private sealed class Near : IEqualityComparer<float>
    {
        public bool Equals(float a, float b) => Math.Abs(a - b) < 1e-4f;
        public int GetHashCode(float value) => 0;
    }
}
