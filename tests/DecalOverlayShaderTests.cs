using System.Numerics;
using CommonFury.Vertices;
using FiveFury;
using FiveFury.Drawables;
using FiveFury.Ydr;
using HikariHopper.Core;
using HikariHopper.Rendering;
using HikariHopper.Tests.Textures;

namespace HikariHopper.Tests;

// GTA V's _texdecal and _vertdecal shaders, which lay a second diffuse map (DiffuseSampler2) over their own on the second
// UV set: by the decal's alpha, or by one less the vertex colour's.
[Collection(NightEmissivesCollection.Name)]
public sealed class DecalOverlayShaderTests
{
    // A flat square facing up, of the given shader, with a mid-grey diffuse map, white vertex colours of the given alpha
    // and, if asked, a red decal of the given alpha on the second UV set.
    private static byte[] Square(GameTarget target, string shader, byte? decal = null, float vertexAlpha = 1)
    {
        Vector3[] positions = [new(-1, -1, 0), new(1, -1, 0), new(1, 1, 0), new(-1, 1, 0)];
        Vector4[] uvs = [new(0, 0, 0, 0), new(1, 0, 0, 0), new(1, 1, 0, 0), new(0, 1, 0, 0)];
        var mesh = new DrawableMeshInput(positions, [0, 1, 2, 0, 2, 3], "square");
        mesh.SetChannel(VertexSemantic.Normal, [.. Enumerable.Repeat(Vector4.UnitZ, 4)]);
        mesh.SetChannel(VertexSemantic.TexCoord0, uvs);
        mesh.SetChannel(VertexSemantic.TexCoord1, uvs);
        mesh.SetChannel(VertexSemantic.Color0, [.. Enumerable.Repeat(new Vector4(1, 1, 1, vertexAlpha), 4)]);
        var material = new DrawableMaterialInput { Name = "square", Shader = shader };
        material.Textures["DiffuseSampler"] = new FiveFury.Textures.TextureReference("grey");
        if (decal is not null)
            material.Textures["DiffuseSampler2"] = new FiveFury.Textures.TextureReference("red");
        var builder = new DrawableBuilder { Name = "square" };
        builder.Materials.Add(material);
        builder.AdditionalTextures.Add(TextureTestKit.Bgra("grey", 4, 4, [64, 64, 64, 255]));
        if (decal is { } alpha)
            builder.AdditionalTextures.Add(TextureTestKit.Bgra("red", 4, 4, [0, 0, 255, alpha]));
        var model = new DrawableModelInput();
        model.Meshes.Add(mesh);
        builder.Models.Add(model);
        return new Ydr(builder.Build(target), target).Write();
    }

    private static SurfaceMaterial Read(byte[] ydr) => Assert.Single(ModelContent.Read("square.ydr", ydr).Surfaces!.Materials);

    // The decal's weight (1 its alpha, 2 one less the vertex alpha) and the UV set it is read on.
    [Theory]
    [InlineData(GameTarget.Legacy, "normal_spec_detail_dpm_texdecal_tnt", 1)]
    [InlineData(GameTarget.Enhanced, "normal_spec_detail_dpm_texdecal_tnt", 1)]
    [InlineData(GameTarget.Legacy, "normal_spec_detail_dpm_vertdecal_tnt", 2)]
    [InlineData(GameTarget.Enhanced, "normal_spec_detail_dpm_vertdecal_tnt", 2)]
    public void A_shader_that_lays_its_own_decal_reads_it_on_the_second_uv_set(GameTarget target, string shader, int weight)
    {
        SurfaceMaterial material = Read(Square(target, shader, decal: 255));
        Assert.NotNull(material.DiffuseLayer);
        Assert.Equal((weight, 1f), (material.Uniforms[42], material.Uniforms[43]));
        // Without its decal map, or a shader laying none, there's nothing to lay.
        Assert.Equal(0f, Read(Square(target, shader)).Uniforms[42]);
        Assert.Equal(0f, Read(Square(target, "normal_spec_detail_dpm_tnt")).Uniforms[42]);
    }

    // The centre's colour as red, green and blue (the frame is read back as blue, green, red).
    private static (int R, int G, int B) Centre(byte[] ydr)
    {
        var (b, g, r) = NightReflectShaderTests.Centre(ydr, 13);
        return (r, g, b);
    }

    [Fact]
    public void A_texdecal_lays_its_decal_by_the_decals_alpha()
    {
        var plain = Centre(Square(GameTarget.Legacy, "normal_spec_detail_dpm_texdecal_tnt"));
        var whole = Centre(Square(GameTarget.Legacy, "normal_spec_detail_dpm_texdecal_tnt", decal: 255));
        var none = Centre(Square(GameTarget.Legacy, "normal_spec_detail_dpm_texdecal_tnt", decal: 0));
        // A whole decal turns the grey red; one of no alpha leaves it as it is.
        Assert.Equal(plain, none);
        Assert.True(whole.R > plain.R + 40 && whole.G < plain.G && whole.B < plain.B, $"{plain} -> {whole}");
    }

    [Fact]
    public void A_vertdecal_lays_its_decal_by_one_less_the_vertex_alpha()
    {
        var plain = Centre(Square(GameTarget.Legacy, "normal_spec_detail_dpm_vertdecal_tnt"));
        // Its own alpha counts for nothing: a decal of no alpha is laid all the same where the vertex alpha is nought.
        var laid = Centre(Square(GameTarget.Legacy, "normal_spec_detail_dpm_vertdecal_tnt", decal: 0, vertexAlpha: 0));
        var kept = Centre(Square(GameTarget.Legacy, "normal_spec_detail_dpm_vertdecal_tnt", decal: 0, vertexAlpha: 1));
        Assert.Equal(plain, kept);
        Assert.True(laid.R > plain.R + 40 && laid.G < plain.G && laid.B < plain.B, $"{plain} -> {laid}");
    }
}
