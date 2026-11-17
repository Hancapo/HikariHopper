using System.Numerics;
using CommonFury.Vertices;
using FiveFury;
using FiveFury.Drawables;
using FiveFury.Ydr;
using HikariHopper.Rendering;
using HikariHopper.Tests.Textures;

namespace HikariHopper.Tests;

public sealed class TreeShaderTests
{
    private static byte[] Tree(GameTarget target, IEnumerable<(string Shader, DrawableMeshInput Mesh, DrawableRenderMask Mask)> parts,
        Action<DrawableMaterialInput>? setup = null)
    {
        var builder = new DrawableBuilder { Name = "tree" };
        builder.AdditionalTextures.Add(TextureTestKit.Bgra("leaves", 4, 4, [40, 160, 60, 255]));
        foreach (var (shader, mesh, mask) in parts)
        {
            var material = new DrawableMaterialInput { Name = mesh.Material, Shader = shader };
            material.Textures["DiffuseSampler"] = new FiveFury.Textures.TextureReference("leaves");
            setup?.Invoke(material);
            builder.Materials.Add(material);
            var model = new DrawableModelInput { RenderMask = mask };
            model.Meshes.Add(mesh);
            builder.Models.Add(model);
        }
        return new Ydr(builder.Build(target), target).Write();
    }

    private static byte[] Tree(GameTarget target, string shader, DrawableMeshInput mesh, Action<DrawableMaterialInput>? setup = null) =>
        Tree(target, [(shader, mesh, DrawableRenderMask.Default | DrawableRenderMask.Shadow)], setup);

    private static DrawableScene Read(byte[] ydr) => ModelContent.Read("tree.ydr", ydr).Surfaces!;

    // A card of leaves two metres square, standing up and facing -Y, its middle at (0, 0, 1).
    private static DrawableMeshInput Card(string name = "card")
    {
        Vector3[] positions = [new(-1, 0, 0), new(1, 0, 0), new(1, 0, 2), new(-1, 0, 2)];
        var mesh = new DrawableMeshInput(positions, [0, 1, 2, 0, 2, 3], name);
        mesh.SetChannel(VertexSemantic.Normal, [.. Enumerable.Repeat(-Vector4.UnitY, 4)]);
        mesh.SetChannel(VertexSemantic.TexCoord0, [new(0, 1, 0, 0), new(1, 1, 0, 0), new(1, 0, 0, 0), new(0, 0, 0, 0)]);
        mesh.SetChannel(VertexSemantic.Color0, [.. Enumerable.Repeat(Vector4.One, 4)]);
        mesh.SetChannel(VertexSemantic.Color1, [.. Enumerable.Repeat(Vector4.One, 4)]);
        mesh.SetChannel(VertexSemantic.Tangent, [.. Enumerable.Repeat(new Vector4(1, 0, 0, 1), 4)]);
        // The centre, where Legacy keeps it.
        mesh.SetChannel(VertexSemantic.Binormal, [.. Enumerable.Repeat(new Vector4(0, 0, 1, 0), 4)]);
        return mesh;
    }

    // A billboard as the game's SLOD trees store theirs: every corner at its middle (0, 0, 1), where it is on the quad in
    // the first UV set, its cell of the atlas in the second, its size in the third and its alignment in the fourth.
    private static DrawableMeshInput Billboard(float alignment = 0)
    {
        Vector3[] positions = [.. Enumerable.Repeat(new Vector3(0, 0, 1), 4)];
        var mesh = new DrawableMeshInput(positions, [0, 1, 2, 0, 2, 3], "billboard");
        mesh.SetChannel(VertexSemantic.Normal, [.. Enumerable.Repeat(-Vector4.UnitY, 4)]);
        mesh.SetChannel(VertexSemantic.Color0, [.. Enumerable.Repeat(Vector4.One, 4)]);
        mesh.SetChannel(VertexSemantic.TexCoord0, [new(0, 1, 0, 0), new(1, 1, 0, 0), new(1, 0, 0, 0), new(0, 0, 0, 0)]);
        mesh.SetChannel(VertexSemantic.TexCoord1, [new(.5f, .75f, 0, 0), new(.75f, .75f, 0, 0), new(.75f, .5f, 0, 0), new(.5f, .5f, 0, 0)]);
        mesh.SetChannel(VertexSemantic.TexCoord2, [.. Enumerable.Repeat(new Vector4(2, 2, 0, 0), 4)]);
        mesh.SetChannel(VertexSemantic.TexCoord3, [.. Enumerable.Repeat(new Vector4(alignment, 1, 0, 0), 4)]);
        return mesh;
    }

    [Theory]
    [InlineData(GameTarget.Legacy, "trees", 1, true)]
    [InlineData(GameTarget.Legacy, "trees_normal_spec", 1, true)]
    [InlineData(GameTarget.Legacy, "trees_normal_spec_camera_facing_tnt", 1, true)]
    [InlineData(GameTarget.Legacy, "trees_lod", 2, true)]
    [InlineData(GameTarget.Legacy, "trees_lod2", 3, false)]
    [InlineData(GameTarget.Enhanced, "trees_normal_spec", 1, true)]
    [InlineData(GameTarget.Enhanced, "trees_lod_tnt", 2, true)]
    [InlineData(GameTarget.Enhanced, "trees_lod2", 3, false)]
    public void The_tree_shaders_are_drawn_with_the_game_s_own_programs_and_cull_as_it_does(GameTarget target, string shader, int program,
        bool culls)
    {
        SurfaceMaterial material = Assert.Single(Read(Tree(target, shader, Card())).Materials);
        Assert.True(material.Rebuilt);
        Assert.Equal(program, (int)material.Tree);
        Assert.Equal(program, material.Uniforms[104]);
        Assert.Equal(culls, material.CullsBack);
        Assert.Equal(shader.Contains("camera"), material.TreeCards);
    }

    [Fact]
    public void A_tree_s_values_read_the_same_from_either_edition()
    {
        float[] Values(GameTarget target) => Assert.Single(Read(Tree(target, "trees_normal_spec", Card(), m =>
        {
            m.Parameters["AlphaTest"] = [.6f];
            m.Parameters["AlphaScale"] = [.7f];
            m.Parameters["SelfShadowing"] = [1];
            m.Parameters["ShadowFalloff"] = [0];
            m.Parameters["UseTreeNormals"] = [.5f];
            m.Parameters["specularIntensityMult"] = [.1f];
            m.Parameters["specularFalloffMult"] = [12];
        })).Materials).Uniforms;
        float[] legacy = Values(GameTarget.Legacy), enhanced = Values(GameTarget.Enhanced);
        Assert.Equal(legacy, enhanced);
        // AlphaScale, AlphaTest; UseTreeNormals, SelfShadowing, ShadowFalloff; the specular constants as the program reads them.
        Assert.Equal([.7f, .6f], legacy[106..108]);
        Assert.Equal([.5f, 1, 0], legacy[108..111]);
        Assert.Equal(.1f, legacy[2], 5);
        Assert.Equal(12, legacy[1]);
        Assert.Equal(.97f, legacy[0], 5);
    }

    [Fact]
    public void A_tree_without_specular_writes_none_and_the_billboards_write_the_game_s_fixed_values()
    {
        float[] lod = Assert.Single(Read(Tree(GameTarget.Legacy, "trees_lod", Card())).Materials).Uniforms;
        Assert.Equal([0, 0, 0], lod[0..3]);
        // SelfShadowing's default.
        Assert.Equal(.8f, lod[109], 5);
        float[] billboard = Assert.Single(Read(Tree(GameTarget.Legacy, "trees_lod2", Billboard(), m => m.Parameters["treeLod2Params"] = [3, 4, 0, 0]))
            .Materials).Uniforms;
        Assert.Equal([.4999f, 512, 0], billboard[0..3]);
        Assert.Equal(1, billboard[109]);
        Assert.Equal([3, 4], billboard[112..114]);
    }

    [Fact]
    public void A_billboard_keeps_its_corners_at_its_middle_with_its_size_and_alignment_for_the_vertex_shader()
    {
        DrawableScene scene = Read(Tree(GameTarget.Legacy, "trees_lod2", Billboard(alignment: 1)));
        SurfaceVertex[] corners = [.. scene.Vertices.Take(4)];
        Assert.All(corners, corner => Assert.Equal(corners[0].Position, corner.Position));
        Assert.All(corners, corner => Assert.Equal(new Vector4(2, 2, 0, 1), corner.Tangent));
        Assert.All(corners, corner => Assert.Equal(1, corner.Uv2.X));
        Assert.Equal(new Vector2(.5f, .75f), corners[0].Uv1);
        Assert.Equal(new Vector2(0, 1), corners[0].Uv0);
    }

    [Fact]
    public void A_camera_card_keeps_its_centre_from_each_vertex()
    {
        DrawableScene scene = Read(Tree(GameTarget.Legacy, "trees_normal_spec_camera_facing", Card()));
        Vector3 centre = new Vector3(0, 0, 1) - scene.Origin;
        foreach (SurfaceVertex corner in scene.Vertices.Take(4))
        {
            Vector3 towards = new(corner.Uv1.X, corner.Uv1.Y, corner.Uv2.X);
            Assert.True(Vector3.Distance(centre, corner.Position + towards) < 1e-4f, $"{corner.Position} + {towards}");
        }
        // A card that isn't turned to the camera keeps its UV sets.
        DrawableScene plain = Read(Tree(GameTarget.Legacy, "trees_normal_spec", Card()));
        Assert.All(plain.Vertices.Take(4), corner => Assert.Equal(Vector2.Zero, corner.Uv1));
    }

    [Fact]
    public void A_shadow_proxy_only_casts_and_the_tree_it_stands_in_for_casts_nothing()
    {
        // As the game's trees have them: the tree drawn in the main view but not into the shadow (0xFD), its proxy only
        // into the shadow (0xE2).
        DrawableScene scene = Read(Tree(GameTarget.Legacy,
        [
            ("trees_normal_spec", Card("leaves"), (DrawableRenderMask)0xFD),
            ("trees_shadow_proxy", Card("proxy"), (DrawableRenderMask)0xE2),
        ]));
        SurfacePart tree = scene.Parts.Single(part => scene.Materials[part.Material].Shader == "trees_normal_spec");
        SurfacePart proxy = scene.Parts.Single(part => scene.Materials[part.Material].Shader == "trees_shadow_proxy");
        Assert.True(tree.Shown);
        Assert.False(tree.Casts);
        Assert.False(proxy.Shown);
        Assert.True(proxy.Casts);
        // A mask that names neither pass, as some made outside the game have, is drawn in both.
        Assert.Equal(DrawableRenderMask.Default | DrawableRenderMask.Shadow, DrawableScene.Passes(DrawableRenderMask.None));
    }

    [Fact]
    public void A_billboard_is_spread_across_the_view_and_the_trees_frames_raise_no_GPU_errors()
    {
        // Without its vertex shader's work a billboard has no area, and the view's middle shows what is behind it.
        var empty = Centre(null, out string? none);
        Assert.Null(none);
        var billboard = Centre(Tree(GameTarget.Legacy, "trees_lod2", Billboard()), out string? error);
        Assert.Null(error);
        Assert.NotEqual(empty, billboard);
        // Every tree program, cards and proxies among them, with the sun's shadow cast.
        foreach (string shader in new[] { "trees", "trees_normal_spec", "trees_normal_spec_camera_facing", "trees_normal_spec_camera_aligned_tnt",
            "trees_lod", "trees_lod_tnt", "trees_shadow_proxy" })
        {
            Centre(Tree(GameTarget.Legacy, shader, Card()), out error);
            Assert.True(error is null, $"{shader}: {error}");
        }
    }

    // The drawable at the middle of a small view, with shadows: the middle's colour, and the frames' GPU error, if any.
    private static (byte R, byte G, byte B) Centre(byte[]? ydr, out string? error)
    {
        GpuDevice gpu = TestGpu.Device();
        const int Size = 64;
        lock (gpu.Gate)
        {
            var renderer = new ViewportRenderer(gpu);
            renderer.Resize(Size, Size);
            renderer.Shadows = true;
            gpu.Device.PushErrorScope(WebGpuSharp.ErrorFilter.Validation);
            if (ydr is not null)
                renderer.Show(ModelContent.Read("tree.ydr", ydr).Surfaces!);
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
            int at = (Size / 2 * Size + Size / 2) * 4;
            return (pixels[at], pixels[at + 1], pixels[at + 2]);
        }
    }
}
