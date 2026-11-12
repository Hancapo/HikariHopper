using System.Numerics;
using CommonFury.Vertices;
using FiveFury;
using FiveFury.Drawables;
using FiveFury.Textures;
using FiveFury.Ydr;
using HikariHopper.Rendering;
using GameFormat = CommonFury.Textures.TextureFormat;

namespace HikariHopper.Tests;

public sealed class PedShaderTests
{
    private static DrawableScene Read(GameTarget target, string shader, Action<DrawableMaterialInput>? setup = null)
    {
        Vector3[] positions = [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0)];
        var mesh = new DrawableMeshInput(positions, [0, 1, 2], "surface");
        mesh.SetChannel(VertexSemantic.Normal, [.. Enumerable.Repeat(Vector4.UnitZ, 3)]);
        mesh.SetChannel(VertexSemantic.TexCoord0, [new(0, 0, 0, 0), new(1, 0, 0, 0), new(1, 1, 0, 0)]);
        mesh.SetChannel(VertexSemantic.TexCoord1, [new(0, 0, 0, 0), new(1, 0, 0, 0), new(1, 1, 0, 0)]);
        // The ped shaders have only skinned layouts: every vertex on the one bone.
        mesh.SetChannel(VertexSemantic.BlendWeights, [.. Enumerable.Repeat(Vector4.UnitX, 3)]);
        mesh.SetChannel(VertexSemantic.BlendIndices, [.. Enumerable.Repeat(Vector4.Zero, 3)]);
        var skeleton = new Skeleton();
        skeleton.Bones.Add(new Bone { Name = "SKEL_ROOT", Tag = 0, Index = 0, ParentIndex = -1 });
        var material = new DrawableMaterialInput { Name = "surface", Shader = shader };
        setup?.Invoke(material);
        var builder = new DrawableBuilder { Name = "ped", Skeleton = skeleton };
        builder.Materials.Add(material);
        var model = new DrawableModelInput();
        model.Meshes.Add(mesh);
        builder.Models.Add(model);
        byte[] ydr = new Ydr(builder.Build(target), target).Write();
        return ModelContent.Read("ped.ydr", ydr).Surfaces!;
    }

    private static SurfaceMaterial Material(GameTarget target, string shader, Action<DrawableMaterialInput>? setup = null) =>
        Assert.Single(Read(target, shader, setup).Materials);

    private static Texture Map(string name) => new(name, 4, 4, GameFormat.BC1Unorm, 1, new byte[8]);

    [Fact]
    public void A_ped_skin_reads_the_same_from_either_edition()
    {
        SurfaceMaterial Skin(GameTarget target) => Material(target, "ped", m =>
        {
            m.Parameters["specularFresnel"] = [.9f];
            m.Parameters["specularFalloffMult"] = [80];
            m.Parameters["specularIntensityMult"] = [.4f];
            m.Parameters["bumpiness"] = [1.2f];
            m.Textures["DiffuseSampler"] = new TextureReference(Map("head_diff_000_a_whi"));
            m.Textures["BumpSampler"] = new TextureReference(Map("head_normal_000"));
            m.Textures["SpecSampler"] = new TextureReference(Map("head_spec_000"));
        });
        SurfaceMaterial legacy = Skin(GameTarget.Legacy), enhanced = Skin(GameTarget.Enhanced);
        foreach (SurfaceMaterial skin in new[] { legacy, enhanced })
        {
            Assert.True(skin.Ped);
            Assert.True(skin.Rebuilt);
            Assert.False(skin.Vehicle);
            float[] u = skin.Uniforms;
            Assert.Equal([.9f, 80, .4f, 1.2f], u[..4]);
            // Skin and clothes, with the rim light and the bend towards the view; not a palette, no hair mask.
            Assert.Equal([1f, 1, 0, 0], u[76..80]);
            // The ped programs cut out at 128 of 255, inclusive.
            Assert.Equal(128.5f / 255, u[12]);
        }
        Assert.Equal(legacy.Uniforms, enhanced.Uniforms);
    }

    [Fact]
    public void A_diffuse_only_ped_shader_writes_no_specular()
    {
        SurfaceMaterial material = Material(GameTarget.Legacy, "ped_default");
        Assert.True(material.Rebuilt);
        Assert.Equal(2, material.Uniforms[76]);
        // The fresnel and nothing else, as PS_DeferredTextured's shaders without specular constants.
        Assert.Equal([.98f, 0, 0], material.Uniforms[..3]);
    }

    [Fact]
    public void Ped_hair_takes_its_highlights_from_the_material_and_cuts_out()
    {
        SurfaceMaterial hair = Material(GameTarget.Legacy, "ped_hair_spiked", m =>
        {
            m.Parameters["anisotropicSpecularIntensity"] = [.2f, .3f];
            m.Parameters["anisotropicSpecularExponent"] = [12, 40];
            m.Parameters["anisotropicSpecularColour"] = [.5f, .4f, .3f, 1];
            m.Parameters["AnisotropicAlphaBias"] = [.75f];
            m.Textures["DiffuseSampler"] = new TextureReference(Map("hair_diff_000_a_uni"));
            m.Textures["AnisoNoiseSpecSampler"] = new TextureReference(Map("hair_noise"));
        });
        Assert.True(hair.Rebuilt);
        Assert.Equal(AlphaMode.Mask, hair.AlphaMode);
        Assert.NotNull(hair.HairNoise);
        // Hair covers a pixel by its alpha: solid only where it is all but opaque, the rest blended in a second pass.
        Assert.True(hair.HairFringe);
        Assert.Equal(SurfaceMaterial.HairSolid, hair.Uniforms[12]);
        float[] u = hair.Uniforms;
        Assert.Equal(3, u[76]);
        Assert.Equal([.2f, .3f, 12, 40], u[80..84]);
        Assert.Equal([.5f, .4f, .3f, .75f], u[84..88]);
        // specularNoiseMapUVScaleFactor at its default.
        Assert.Equal([2f, 1, 3, 1], u[88..92]);
    }

    [Fact]
    public void The_forward_ped_shader_has_no_rim_light_and_blends()
    {
        SurfaceMaterial glass = Material(GameTarget.Legacy, "ped_alpha");
        Assert.True(glass.Rebuilt);
        Assert.Equal(AlphaMode.Blend, glass.AlphaMode);
        Assert.Equal([1f, 0], glass.Uniforms[76..78]);
    }

    [Fact]
    public void A_palette_ped_shader_reads_its_diffuse_alpha_as_an_index()
    {
        Assert.Equal(1, Material(GameTarget.Legacy, "ped_palette").Uniforms[78]);
        Assert.Equal(1, Material(GameTarget.Legacy, "ped_default_palette").Uniforms[78]);
        Assert.Equal(0, Material(GameTarget.Legacy, "ped_wrinkle").Uniforms[78]);
    }

    [Fact]
    public void A_ped_shader_not_rebuilt_yet_is_drawn_as_the_closest_ped_shader()
    {
        DrawableScene scene = Read(GameTarget.Legacy, "ped_fur");
        SurfaceMaterial fur = Assert.Single(scene.Materials);
        Assert.True(fur.Ped);
        Assert.False(fur.Rebuilt);
        Assert.Equal(1, fur.Uniforms[76]);
        Assert.Contains("Not rebuilt yet, drawn as the closest ped shader: ped_fur", scene.Diagnostics);
        Assert.DoesNotContain(scene.Diagnostics, line => line.Contains("default material"));
    }

}
