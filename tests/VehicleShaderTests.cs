using System.Numerics;
using CommonFury.Vertices;
using FiveFury;
using FiveFury.Drawables;
using FiveFury.Ydr;
using HikariHopper.Rendering;

namespace HikariHopper.Tests;

public sealed class VehicleShaderTests
{
    private static SurfaceMaterial Read(GameTarget target, string shader, Action<DrawableMaterialInput>? setup = null)
    {
        Vector3[] positions = [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0)];
        var mesh = new DrawableMeshInput(positions, [0, 1, 2], "surface");
        mesh.SetChannel(VertexSemantic.Normal, [.. Enumerable.Repeat(Vector4.UnitZ, 3)]);
        var material = new DrawableMaterialInput { Name = "surface", Shader = shader };
        setup?.Invoke(material);
        var builder = new DrawableBuilder { Name = "car" };
        builder.Materials.Add(material);
        var model = new DrawableModelInput();
        model.Meshes.Add(mesh);
        builder.Models.Add(model);
        byte[] ydr = new Ydr(builder.Build(target), target).Write();
        return Assert.Single(ModelContent.Read("car.ydr", ydr).Surfaces!.Materials);
    }

    [Fact]
    public void A_vehicle_colour_that_names_a_paint_slot_is_shown_as_the_game_fills_it_with_no_vehicle_colours()
    {
        // (2, n, n) names paint slot n - 1: grey 0x96 with no vehicle's colours, but the fifth slot, which stays white.
        Vector3 grey = new(150 / 255f);
        Assert.Equal(grey, SurfaceMaterial.PaintColour([2, 1, 1]));
        Assert.Equal(grey, SurfaceMaterial.PaintColour([2, 2, 2]));
        Assert.Equal(Vector3.One, SurfaceMaterial.PaintColour([2, 5, 5]));
        Assert.Equal(grey, SurfaceMaterial.PaintColour([2, 7, 7]));
        // Anything else is a colour.
        Assert.Equal(new Vector3(.2f, .3f, .4f), SurfaceMaterial.PaintColour([.2f, .3f, .4f]));
        Assert.Equal(new Vector3(2, 9, 9), SurfaceMaterial.PaintColour([2, 9, 9]));
    }

    [Fact]
    public void A_vehicle_paint_reads_the_same_from_either_edition()
    {
        SurfaceMaterial Paint(GameTarget target) => Read(target, "vehicle_paint1", m =>
        {
            m.Parameters["matDiffuseColor"] = [2, 1, 1];
            m.Parameters["specularFresnel"] = [.97f];
            m.Parameters["specularFalloffMult"] = [180];
            m.Parameters["specularIntensityMult"] = [.15f];
            m.Parameters["specular2Color_DirLerp"] = [0, .5f, 0, .25f];
            m.Parameters["dirtLevelMod"] = [1, 1, 1, 1];
        });
        SurfaceMaterial legacy = Paint(GameTarget.Legacy), enhanced = Paint(GameTarget.Enhanced);
        foreach (SurfaceMaterial paint in new[] { legacy, enhanced })
        {
            Assert.True(paint.Vehicle);
            Assert.True(paint.Rebuilt);
            float[] u = paint.Uniforms;
            Assert.Equal([.97f, 180, .15f], u[..3]);
            // The primary slot as the game fills it with no vehicle's colours; clean, whatever the material's dirt level.
            const float grey = 150 / 255f;
            Assert.Equal([grey, grey, grey], u[16..19]);
            Assert.Equal(0, u[20]);
            // The second lobe in the pearlescent slot's colour, swinging overhead as the material says.
            Assert.Equal([grey, grey, grey, .25f], u[28..32]);
            // A paint with the second lobe. vehicle_paint1's program writes its intensity from the constant alone
            // (no map), less the dirt's roughness, and its power from the constant alone.
            Assert.Equal(1, u[32]);
            Assert.Equal(1, u[33]);
            Assert.Equal([0f, 1, 0, 0], u[36..40]);
            Assert.Equal([0f, 1], u[40..42]);
            // A vehicle's vertex blue gates its dirt, never an emissive mask.
            Assert.Equal(0, u[11]);
        }
        Assert.Equal(legacy.Uniforms, enhanced.Uniforms);
    }

    [Fact]
    public void A_vehicle_mesh_takes_its_specular_from_its_map()
    {
        // vehicle_mesh: 0.6 times R², less the dirt's roughness; power 460 times G²; fresnel one less B.
        float[] u = Read(GameTarget.Enhanced, "vehicle_mesh").Uniforms;
        Assert.Equal(1, u[15]);
        Assert.Equal(0, u[32]);
        Assert.Equal(.6f, u[2], 5);
        Assert.Equal(460, u[1]);
        Assert.Equal([2f, 1, 2, 1], u[36..40]);
    }

    [Fact]
    public void A_livery_layer_lies_over_the_paint_on_the_second_uv_set()
    {
        // vehicle_paint3: matDiffuseColor2 colours the second diffuse layer and its w sets how strongly it covers the
        // paint; the layer is read on the second UV set (vehicle_cloth2 reads it on the first). Without a layer
        // texture there is nothing to lay over.
        float[] u = Read(GameTarget.Enhanced, "vehicle_paint3", m => m.Parameters["matDiffuseColor2"] = [.2f, .4f, .6f, .8f]).Uniforms;
        Assert.Equal([.2f, .4f, .6f, .8f], u[44..48]);
        Assert.Equal(0, u[42]);
        Assert.Equal(1, u[43]);
        Assert.Equal(0, Read(GameTarget.Enhanced, "vehicle_cloth2").Uniforms[43]);
    }

    [Theory]
    // Each shader's own program: the scale, map, roughness, power and its scale, fresnel, UV set.
    [InlineData("vehicle_paint2", -1f, 1, 1, 1, -1f, -1f, 0)]
    [InlineData("vehicle_paint3", -1f, 1, 1, 1, -1f, -1f, 1)]
    [InlineData("vehicle_basic", -1f, 1, 2, 1, -1f, -1f, 0)]
    [InlineData("vehicle_shuts", .065f, 1, 2, 1, 179f, .83f, 0)]
    [InlineData("vehicle_emissive_opaque", .8f, 1, 1, 1, 510f, .96f, 0)]
    [InlineData("vehicle_tire", 1f, 2, 1, 2, 512f, -2f, 0)]
    [InlineData("vehicle_interior2", 0f, 1, 1, 1, 10f, -1f, 0)]
    public void Each_vehicle_shader_writes_its_specular_as_its_program_does(string shader, float scale, int map, int rough, int power,
        float powerScale, float fresnel, int uv)
    {
        // -1 stands for the material's own constant, set here; -2 for none (fresnel from the map).
        SurfaceMaterial material = Read(GameTarget.Enhanced, shader, m =>
        {
            if (scale == -1)
                m.Parameters["specularIntensityMult"] = [.4f];
            if (powerScale == -1)
                m.Parameters["specularFalloffMult"] = [90];
            if (fresnel == -1)
                m.Parameters["specularFresnel"] = [.9f];
        });
        float[] u = material.Uniforms;
        Assert.Equal(scale == -1 ? .4f : scale, u[2], 5);
        Assert.Equal(powerScale == -1 ? 90 : powerScale, u[1], 5);
        if (fresnel != -2)
            Assert.Equal(fresnel == -1 ? .9f : fresnel, u[0], 5);
        Assert.Equal([map, rough, power], u[36..39]);
        Assert.Equal(uv, u[40]);
    }
}
