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
    private static byte[] Quad(GameTarget target, string shader, Action<DrawableMaterialInput>? setup = null)
    {
        Vector3[] positions = [new(0, 0, 0), new(2, 0, 0), new(2, 1, 0), new(0, 1, 0)];
        var mesh = new DrawableMeshInput(positions, [0, 1, 2, 0, 2, 3], "surface");
        mesh.SetChannel(VertexSemantic.Normal, [.. Enumerable.Repeat(Vector4.UnitZ, 4)]);
        mesh.SetChannel(VertexSemantic.TexCoord0, [new(0, 0, 0, 0), new(1, 0, 0, 0), new(1, 1, 0, 0), new(0, 1, 0, 0)]);
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
            Assert.Equal(lod ? [null, null, null, null] : ["ground_0_n", "ground_1_n", "ground_2_n", "ground_3_n"], material.Layers[4..].Select(t => t?.Name));
            Assert.Equal(weights > 0 ? "ground_lookup" : null, material.Lookup?.Name);
            // terrain.wgsl's Terrain uniform: specularIntensityMult (squared there), the falloff, bumpiness, the program's
            // fixed fresnel; where the weights come from and whether there are bump maps.
            Assert.Equal([.3f, 20, lod ? 1 : .7f, .98f, weights, bumps], material.Uniforms[..6]);
        }
        Assert.Equal(legacy.Uniforms, enhanced.Uniforms);
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
        DrawableScene scene = ModelContent.Read("ground.ydr", Quad(GameTarget.Legacy, "terrain_cb_w_4lyr_pxm")).Surfaces!;
        SurfaceMaterial material = scene.Materials.Single();
        Assert.True(material.Terrain);
        Assert.False(material.Rebuilt);
        Assert.Contains("Not rebuilt yet, drawn as the closest terrain shader: terrain_cb_w_4lyr_pxm", scene.Diagnostics);
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
