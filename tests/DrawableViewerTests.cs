using System.Numerics;
using CommonFury.Vertices;
using FiveFury;
using FiveFury.Drawables;
using FiveFury.Textures;
using FiveFury.Ydr;
using HikariHopper.Rendering;
using GameFormat = CommonFury.Textures.TextureFormat;

namespace HikariHopper.Tests;

public sealed class DrawableViewerTests
{
    private static byte[] Quad(GameTarget target, string shader, Action<DrawableMaterialInput>? setup = null, Action<DrawableMeshInput>? channels = null)
    {
        Vector3[] positions = [new(0, 0, 0), new(2, 0, 0), new(2, 1, 0), new(0, 1, 0)];
        var mesh = new DrawableMeshInput(positions, [0, 1, 2, 0, 2, 3], "surface");
        mesh.SetChannel(VertexSemantic.Normal, [.. Enumerable.Repeat(Vector4.UnitZ, 4)]);
        mesh.SetChannel(VertexSemantic.TexCoord0, [new(0, 0, 0, 0), new(1, 0, 0, 0), new(1, 1, 0, 0), new(0, 1, 0, 0)]);
        channels?.Invoke(mesh);
        var material = new DrawableMaterialInput { Name = "surface", Shader = shader };
        setup?.Invoke(material);
        var builder = new DrawableBuilder { Name = "quad" };
        builder.Materials.Add(material);
        var model = new DrawableModelInput();
        model.Meshes.Add(mesh);
        builder.Models.Add(model);
        return new Ydr(builder.Build(target), target).Write();
    }

    private static Texture Map(string name) => new(name, 4, 4, GameFormat.BC1Unorm, 1, new byte[8]);

    [Fact]
    public void A_material_reads_the_same_from_either_edition()
    {
        SurfaceMaterial Read(GameTarget target)
        {
            byte[] ydr = Quad(target, "normal_spec", material =>
            {
                material.Parameters["specularFresnel"] = [.8f];
                material.Parameters["specularFalloffMult"] = [5];
                material.Parameters["specularIntensityMult"] = [.53f];
                material.Parameters["bumpiness"] = [1.5f];
                material.Textures["DiffuseSampler"] = new TextureReference(Map("barrel_d"));
                material.Textures["BumpSampler"] = new TextureReference(Map("barrel_n"));
                material.Textures["SpecSampler"] = new TextureReference(Map("barrel_s"));
            });
            ModelContent content = ModelContent.Read("quad.ydr", ydr);
            DrawableScene scene = content.Surfaces!;
            Assert.Equal(2, scene.TriangleCount);
            // A quad: four corners, and five edges, the diagonal shared by both triangles.
            Assert.Equal(4, scene.VertexCount);
            Assert.Equal(5, scene.EdgeCount);
            Assert.Equal(new Vector3(1, .5f, 0), scene.Origin);
            return Assert.Single(scene.Materials);
        }

        SurfaceMaterial legacy = Read(GameTarget.Legacy), enhanced = Read(GameTarget.Enhanced);
        foreach (SurfaceMaterial material in new[] { legacy, enhanced })
        {
            Assert.Equal("normal_spec", material.Shader);
            Assert.True(material.Rebuilt);
            Assert.Equal(AlphaMode.Opaque, material.AlphaMode);
            Assert.Equal(("barrel_d", "barrel_n", "barrel_s"), (material.Diffuse?.Name, material.Normal?.Name, material.Specular?.Name));
            Assert.Equal([.8f, 5, .53f, 1.5f], material.Uniforms[..4]);
            // Reads its normal and specular maps.
            Assert.Equal([1f, 1f], material.Uniforms[8..10]);
        }
        Assert.Equal(legacy.Uniforms, enhanced.Uniforms);
    }

    [Theory]
    [InlineData("normal_spec_detail", 1f)]
    [InlineData("normal_diffspec_detail", 2f)]
    [InlineData("normal_detail", 0f)]
    public void A_detail_shader_reads_its_detail_map_from_either_edition(string shader, float mask)
    {
        SurfaceMaterial Read(GameTarget target) => ModelContent.Read("wall.ydr", Quad(target, shader, material =>
        {
            material.Parameters["detailSettings"] = [1.2f, .3f, 11.6f, 10.4f];
            material.Textures["DiffuseSampler"] = new TextureReference(Map("wall_d"));
            material.Textures["BumpSampler"] = new TextureReference(Map("wall_n"));
            material.Textures["DetailSampler"] = new TextureReference(Map("env_noise_05"));
            if (mask == 1)
                material.Textures["SpecSampler"] = new TextureReference(Map("wall_s"));
        })).Surfaces!.Materials.Single();

        SurfaceMaterial legacy = Read(GameTarget.Legacy), enhanced = Read(GameTarget.Enhanced);
        foreach (SurfaceMaterial material in new[] { legacy, enhanced })
        {
            Assert.Equal("env_noise_05", material.Detail?.Name);
            // surface.wgsl's detail and detail_options: detailSettings, then has a detail map and what masks it (the
            // specular map's alpha, the diffuse map's for diffspec, or nothing).
            Assert.Equal([1.2f, .3f, 11.6f, 10.4f, 1, mask], material.Uniforms[48..54]);
        }
        Assert.Equal(legacy.Uniforms, enhanced.Uniforms);
    }

    [Fact]
    public void A_detail_map_the_drawable_and_its_dictionaries_lack_is_missing_and_left_out()
    {
        SurfaceMaterial material = ModelContent.Read("wall.ydr", Quad(GameTarget.Legacy, "normal_spec_detail",
            m => m.Textures["DetailSampler"] = new TextureReference("env_noise_05"))).Surfaces!.Materials.Single();
        Assert.Null(material.Detail);
        Assert.Contains("env_noise_05", material.MissingTextures);
        Assert.Equal(0, material.Uniforms[52]);
    }

    [Fact]
    public void A_diffspec_shader_takes_its_specular_from_the_diffuse_colour_in_either_edition()
    {
        SurfaceMaterial Read(GameTarget target) => ModelContent.Read("wall.ydr", Quad(target, "normal_diffspec", material =>
        {
            material.Parameters["SpecDesaturateIntensity"] = [.2f, .7f, .1f, .5f];
            material.Parameters["SpecDesaturateExponent"] = [.3f, .3f, .4f, .8f];
        })).Surfaces!.Materials.Single();
        SurfaceMaterial legacy = Read(GameTarget.Legacy), enhanced = Read(GameTarget.Enhanced);
        foreach (SurfaceMaterial material in new[] { legacy, enhanced })
        {
            // surface.wgsl's spec_source (the diffuse colour), then SpecDesaturateIntensity and SpecDesaturateExponent.
            Assert.Equal(2, material.Uniforms[56]);
            Assert.Equal([.2f, .7f, .1f, .5f, .3f, .3f, .4f, .8f], material.Uniforms[60..68]);
        }
        Assert.Equal(legacy.Uniforms, enhanced.Uniforms);
    }

    [Theory]
    [InlineData("emissive_speclum", 1f)]
    [InlineData("normal_spec", 0f)]
    public void A_shader_says_where_its_specular_comes_from(string shader, float source) =>
        Assert.Equal(source, ModelContent.Read("wall.ydr", Quad(GameTarget.Legacy, shader)).Surfaces!.Materials.Single().Uniforms[56]);

    [Fact]
    public void A_vertex_normal_too_short_to_point_anywhere_is_taken_as_up()
    {
        byte[] ydr = Quad(GameTarget.Legacy, "default", channels: mesh =>
            mesh.SetChannel(VertexSemantic.Normal, [new(.1f, .1f, .1f, 0), new(1, 0, 0, 0), new(1, 0, 0, 0), new(1, 0, 0, 0)]));
        SurfaceVertex[] vertices = ModelContent.Read("wall.ydr", ydr).Surfaces!.Vertices;
        Assert.Equal(Vector3.UnitZ, vertices[0].Normal);
        Assert.Equal(Vector3.UnitX, vertices[1].Normal);
    }

    [Theory]
    [InlineData("normal_spec_pxm", .6f)]
    [InlineData("normal_decal_pxm", 0f)]
    public void A_parallax_surface_reads_its_height_map_from_either_edition(string shader, float selfShadow)
    {
        SurfaceMaterial Read(GameTarget target) => ModelContent.Read("wall.ydr", Quad(target, shader, material =>
        {
            material.Parameters["heightScale"] = [.04f];
            material.Parameters["heightBias"] = [.02f];
            material.Parameters["parallaxSelfShadowAmount"] = [.6f];
            material.Textures["heightSampler"] = new TextureReference(Map("wall_h"));
        })).Surfaces!.Materials.Single();
        SurfaceMaterial legacy = Read(GameTarget.Legacy), enhanced = Read(GameTarget.Enhanced);
        foreach (SurfaceMaterial material in new[] { legacy, enhanced })
        {
            Assert.Equal("wall_h", material.Height?.Name);
            // surface.wgsl's parallax: has it, heightScale, heightBias, the self-shadow (none from the decals'
            // programs); globalHeightScale.
            Assert.Equal([1, .04f, .02f, selfShadow, 1], material.Uniforms[68..73]);
        }
        Assert.Equal(legacy.Uniforms, enhanced.Uniforms);
    }

    [Fact]
    public void A_surface_without_parallax_has_none()
    {
        SurfaceMaterial material = ModelContent.Read("wall.ydr", Quad(GameTarget.Legacy, "normal_spec")).Surfaces!.Materials.Single();
        Assert.Equal([0f, 0, 0, 0, 0], material.Uniforms[68..73]);
    }

    [Fact]
    public void A_vertex_without_a_fourth_uv_set_reads_its_third_as_the_parallax_controls()
    {
        byte[] ydr = Quad(GameTarget.Legacy, "normal_spec_pxm", channels: mesh =>
        {
            mesh.SetChannel(VertexSemantic.TexCoord1, [.. Enumerable.Repeat(new Vector4(5, 6, 0, 0), 4)]);
            mesh.SetChannel(VertexSemantic.TexCoord2, [.. Enumerable.Repeat(new Vector4(.1f, .9f, 0, 0), 4)]);
        });
        Assert.All(ModelContent.Read("wall.ydr", ydr).Surfaces!.Vertices, vertex => Assert.Equal(new Vector2(.1f, .9f), vertex.Uv2));
    }

    [Fact]
    public void A_drawables_embedded_textures_are_counted_and_read_each_once()
    {
        byte[] ydr = Quad(GameTarget.Enhanced, "normal_spec", material =>
        {
            material.Textures["DiffuseSampler"] = new TextureReference(Map("sign_d"));
            material.Textures["BumpSampler"] = new TextureReference(Map("sign_n"));
            material.Textures["SpecSampler"] = new TextureReference("shared_s");
        });
        // Two embedded; the third is only named, found elsewhere.
        Assert.Equal(2, ModelContent.Read("sign.ydr", ydr).Surfaces!.EmbeddedTextureCount);
        (IReadOnlyList<Texture> textures, GameTarget target) = ModelContent.EmbeddedTextures("sign.ydr", ydr);
        Assert.Equal(["sign_d", "sign_n"], textures.Select(t => t.Name).Order());
        Assert.Equal(GameTarget.Enhanced, target);
        Assert.Throws<InvalidDataException>(() => ModelContent.EmbeddedTextures("sign.ybn", ydr));
    }

    [Fact]
    public void A_shader_without_specular_constants_writes_none()
    {
        SurfaceMaterial material = ModelContent.Read("quad.ydr", Quad(GameTarget.Enhanced, "default")).Surfaces!.Materials.Single();
        // PS_DeferredTextured for default: no specular, a fresnel of 0.98.
        Assert.Equal([.98f, 0, 0], material.Uniforms[..3]);
        Assert.Equal(0, material.Uniforms[9]);
    }

    [Fact]
    public void A_tinted_shader_reads_its_palette_instead_of_glowing()
    {
        SurfaceMaterial material = ModelContent.Read("quad.ydr", Quad(GameTarget.Legacy, "normal_spec_tnt", m =>
        {
            m.Textures["TintPaletteSampler"] = new TextureReference(Map("barrel_pal"));
            m.Parameters["tintPaletteSelector"] = [.25f];
        })).Surfaces!.Materials.Single();
        Assert.Equal("barrel_pal", material.TintPalette?.Name);
        Assert.Equal([1f, .25f], material.Uniforms[13..15]);
        Assert.Equal(0, material.Uniforms[11]);
    }

    [Theory]
    [InlineData("terrain_cb_w_4lyr", 0f, 1f)]
    [InlineData("terrain_cb_w_4lyr_2tex_blend", 1f, 1f)]
    [InlineData("terrain_cb_w_4lyr_cm", 2f, 1f)]
    [InlineData("terrain_cb_w_4lyr_lod", 0f, 0f)]
    public void A_terrain_shader_reads_its_four_layers_from_either_edition(string shader, float weights, float bumps)
    {
        bool lod = bumps == 0;
        SurfaceMaterial Read(GameTarget target) => ModelContent.Read("ground.ydr", Quad(target, shader, material =>
        {
            material.Parameters["specularIntensityMult"] = [.3f];
            material.Parameters["specularFalloffMult"] = [20];
            if (!lod)
                material.Parameters["bumpiness"] = [.7f];
            for (int i = 0; i < 4; i++)
            {
                material.Textures[$"TextureSampler_layer{i}"] = new TextureReference(Map($"ground_{i}"));
                if (!lod)
                    material.Textures[$"BumpSampler_layer{i}"] = new TextureReference(Map($"ground_{i}_n"));
            }
            if (weights > 0)
                material.Textures["lookupSampler"] = new TextureReference(Map("ground_lookup"));
        })).Surfaces!.Materials.Single();

        SurfaceMaterial legacy = Read(GameTarget.Legacy), enhanced = Read(GameTarget.Enhanced);
        foreach (SurfaceMaterial material in new[] { legacy, enhanced })
        {
            Assert.True(material.Terrain);
            Assert.True(material.Rebuilt);
            Assert.Equal(["ground_0", "ground_1", "ground_2", "ground_3"], material.Layers[..4].Select(t => t?.Name));
            Assert.Equal(lod ? [null, null, null, null] : ["ground_0_n", "ground_1_n", "ground_2_n", "ground_3_n"], material.Layers[4..8].Select(t => t?.Name));
            Assert.Equal(weights > 0 ? "ground_lookup" : null, material.Lookup?.Name);
            // terrain.wgsl's Terrain uniform: specularIntensityMult (squared there), the falloff, bumpiness, the program's
            // fixed fresnel; where the weights come from and whether there are bump maps.
            Assert.Equal([.3f, 20, lod ? 1 : .7f, .98f, weights, bumps], material.Uniforms[..6]);
        }
        Assert.Equal(legacy.Uniforms, enhanced.Uniforms);
    }

    [Theory]
    [InlineData("terrain_cb_w_4lyr_pxm", 0f, 0f, 0f)]
    [InlineData("terrain_cb_w_4lyr_2tex_blend_pxm_spm", 1f, 0f, 1f)]
    [InlineData("terrain_cb_w_4lyr_2tex_pxm", 0f, 1f, 0f)]
    [InlineData("terrain_cb_w_4lyr_cm_pxm", 2f, 0f, 0f)]
    public void A_parallax_terrain_shader_reads_its_height_maps_from_either_edition(string shader, float weights, float second, float specularMap)
    {
        SurfaceMaterial Read(GameTarget target) => ModelContent.Read("ground.ydr", Quad(target, shader, material =>
        {
            material.Parameters["parallaxSelfShadowAmount"] = [.8f];
            material.Parameters["bumpSelfShadowAmount"] = [.25f];
            for (int i = 0; i < 4; i++)
            {
                material.Parameters[$"heightScale{i}"] = [.01f * (i + 1)];
                material.Parameters[$"heightBias{i}"] = [.005f * (i + 1)];
                material.Textures[$"TextureSampler_layer{i}"] = new TextureReference(Map($"ground_{i}"));
                material.Textures[$"BumpSampler_layer{i}"] = new TextureReference(Map($"ground_{i}_n"));
                material.Textures[$"heightMapSamplerLayer{i}"] = new TextureReference(Map($"ground_{i}_h"));
            }
            if (specularMap > 0)
            {
                material.Parameters["specularIntensityMultSpecMap"] = [.4f];
                material.Parameters["specIntensityAdjust"] = [.6f];
                material.Parameters["specularFalloffMultSpecMap"] = [30];
                material.Parameters["specFalloffAdjust"] = [.5f];
            }
            if (weights > 0)
                material.Textures["lookupSampler"] = new TextureReference(Map("ground_lookup"));
        })).Surfaces!.Materials.Single();

        SurfaceMaterial legacy = Read(GameTarget.Legacy), enhanced = Read(GameTarget.Enhanced);
        foreach (SurfaceMaterial material in new[] { legacy, enhanced })
        {
            Assert.True(material.Rebuilt);
            Assert.Equal(["ground_0_h", "ground_1_h", "ground_2_h", "ground_3_h"], material.Layers[8..].Select(t => t?.Name));
            Assert.Equal(weights, material.Uniforms[4]);
            // terrain.wgsl's layout: layers 1 to 3 on the second UV set (_2tex, not _2tex_blend), bumpSelfShadowAmount,
            // has parallax, parallaxSelfShadowAmount; then each layer's height scale and bias.
            Assert.Equal([second, .25f, 1, .8f], material.Uniforms[8..12]);
            Assert.Equal([.01f, .02f, .03f, .04f, .005f, .01f, .015f, .02f], material.Uniforms[12..20]);
            // The _spm shaders' specular map in the height maps' green; and globalHeightScale.
            Assert.Equal(specularMap, material.Uniforms[20]);
            if (specularMap > 0)
                Assert.Equal([.4f, .6f, 30, .5f], material.Uniforms[21..25]);
            Assert.Equal(1, material.Uniforms[25]);
        }
        Assert.Equal(legacy.Uniforms, enhanced.Uniforms);
    }

    [Fact]
    public void A_terrain_shader_without_parallax_has_none_and_keeps_its_layers_on_the_first_uv_set()
    {
        SurfaceMaterial material = ModelContent.Read("ground.ydr", Quad(GameTarget.Legacy, "terrain_cb_w_4lyr_2tex_blend")).Surfaces!.Materials.Single();
        Assert.Equal([0f, .3f, 0, 0], material.Uniforms[8..12]);
        Assert.Equal(0, material.Uniforms[20]);
    }

    [Fact]
    public void A_vertex_reads_its_fourth_uv_set_as_the_parallax_controls()
    {
        byte[] ydr = Quad(GameTarget.Legacy, "terrain_cb_w_4lyr_pxm", channels: mesh =>
            mesh.SetChannel(VertexSemantic.TexCoord3, [.. Enumerable.Repeat(new Vector4(.25f, .75f, 0, 0), 4)]));
        Assert.All(ModelContent.Read("ground.ydr", ydr).Surfaces!.Vertices, vertex => Assert.Equal(new Vector2(.25f, .75f), vertex.Uv2));
    }

    [Fact]
    public void A_mesh_without_a_second_vertex_colour_reads_it_as_nothing()
    {
        // The default shader's layout has no second colour. Read as nothing, terrain weights draw the first layer (or
        // the lookup map); the terrain shaders' own layouts always carry it.
        DrawableScene scene = ModelContent.Read("bench.ydr", Quad(GameTarget.Legacy, "default")).Surfaces!;
        Assert.All(scene.Vertices, vertex => Assert.Equal(0u, vertex.Colour1));
    }

    [Fact]
    public void A_terrain_shader_not_rebuilt_yet_is_drawn_as_the_closest_terrain()
    {
        DrawableScene scene = ModelContent.Read("ground.ydr", Quad(GameTarget.Legacy, "terrain_cb_w_4lyr_2tex_blend_ttn")).Surfaces!;
        SurfaceMaterial material = scene.Materials.Single();
        Assert.True(material.Terrain);
        Assert.False(material.Rebuilt);
        Assert.Contains("Not rebuilt yet, drawn as the closest terrain shader: terrain_cb_w_4lyr_2tex_blend_ttn", scene.Diagnostics);
    }

    [Fact]
    public void Textures_missing_from_the_drawable_come_from_the_dictionary_beside_it()
    {
        byte[] ydr = Quad(GameTarget.Legacy, "default", material => material.Textures["DiffuseSampler"] = new TextureReference("bench_d"));
        Assert.Equal(["bench_d"], ModelContent.Read("bench.ydr", ydr).Surfaces!.Materials.Single().MissingTextures);

        var dictionary = new TextureDictionary();
        dictionary.Textures.Add(Map("bench_d"));
        byte[] ytd = new FiveFury.Ytd.Ytd(dictionary).Write();
        SurfaceMaterial material = ModelContent.Read("bench.ydr", ydr, [ytd]).Surfaces!.Materials.Single();
        Assert.Equal("bench_d", material.Diffuse?.Name);
        Assert.Empty(material.MissingTextures);
    }
}
