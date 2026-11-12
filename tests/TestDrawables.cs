using System.Numerics;
using CommonFury.Numerics;
using CommonFury.Vertices;
using FiveFury;
using FiveFury.Bounds;
using FiveFury.Drawables;
using FiveFury.Textures;

namespace HikariHopper.Tests;

/// <summary>Drawables made in a test, for what doesn't need a real model.</summary>
internal static class TestDrawables
{
    /// <summary>
    /// One triangle, (0,0,0) (1,0,0) (0,1,0), drawn with one material called "surface", written as a Legacy .ydr. With
    /// <paramref name="diffuse"/>, the material's DiffuseSampler is that texture, embedded in the drawable; with
    /// <paramref name="diffuseName"/>, it names a texture to be found elsewhere. With <paramref name="bound"/>, the
    /// drawable carries a collision box around it.
    /// </summary>
    internal static byte[] Triangle(string name = "tri", string shader = "default", Texture? diffuse = null, string? diffuseName = null,
        bool bound = false)
    {
        var mesh = new DrawableMeshInput([new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], [0, 1, 2], "surface");
        var material = new DrawableMaterialInput { Name = "surface", Shader = shader };
        if (diffuse is not null || diffuseName is not null)
        {
            // A textured material needs a UV set.
            mesh.SetChannel(VertexSemantic.TexCoord0, [new(0, 0, 0, 0), new(1, 0, 0, 0), new(0, 1, 0, 0)]);
            material.Textures["DiffuseSampler"] = diffuse is not null ? new TextureReference(diffuse) : new TextureReference(diffuseName!);
        }
        var builder = new DrawableBuilder { Name = name };
        if (bound)
            builder.Bound = new BoundBox(new Aabb3(new Vector3(0), new Vector3(1)));
        builder.Materials.Add(material);
        var model = new DrawableModelInput();
        model.Meshes.Add(mesh);
        builder.Models.Add(model);
        return new FiveFury.Ydr.Ydr(builder.Build(GameTarget.Legacy), GameTarget.Legacy).Write();
    }
}
