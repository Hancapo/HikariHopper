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
    public void A_vehicle_colour_that_names_a_paint_layer_is_shown_in_that_layers_preview_colour()
    {
        // (2, n, n) names a paint layer the game fills in; 5 is the default one, which isn't paintable.
        Assert.Equal(Vector3.One, SurfaceMaterial.PaintColour([2, 1, 1]));
        Assert.Equal(Vector3.One, SurfaceMaterial.PaintColour([2, 2, 2]));
        Assert.Equal(new Vector3(.5f), SurfaceMaterial.PaintColour([2, 5, 5]));
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
            // The primary layer's preview colour; clean, whatever the material's dirt level.
            Assert.Equal([1f, 1, 1], u[16..19]);
            Assert.Equal(0, u[20]);
            // The second lobe in the pearlescent layer's colour, swinging overhead as the material says.
            Assert.Equal([1f, 1, 1, .25f], u[28..32]);
            // A paint, with the second lobe, taking its specular from constants.
            Assert.Equal(1, u[32]);
            Assert.Equal(1, u[33]);
            Assert.Equal(1, u[35]);
            // A vehicle's vertex blue is its burn level, never an emissive mask.
            Assert.Equal(0, u[11]);
        }
        Assert.Equal(legacy.Uniforms, enhanced.Uniforms);
    }

    [Fact]
    public void A_vehicle_mesh_takes_its_specular_from_its_map()
    {
        float[] u = Read(GameTarget.Enhanced, "vehicle_mesh").Uniforms;
        Assert.Equal(1, u[15]);
        Assert.Equal(0, u[32]);
        Assert.Equal(0, u[35]);
    }
}
