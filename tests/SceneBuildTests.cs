using System.Numerics;
using CommonFury.Vertices;
using FiveFury;
using FiveFury.Drawables;
using FiveFury.Ydr;
using HikariHopper.Rendering;

namespace HikariHopper.Tests;

public sealed class SceneBuildTests
{
    [Fact]
    public void Edges_are_counted_once_each_however_many_triangles_share_them()
    {
        var random = new Random(7);
        for (int round = 0; round < 50; round++)
        {
            var indices = new List<uint> { 99, 98, 97 };
            int triangles = random.Next(1, 400);
            for (int i = 0; i < triangles * 3; i++)
                indices.Add((uint)random.Next(0, 60));
            // Counted as a set of edges would count them, over the part after the three in front.
            var expected = new HashSet<(uint, uint)>();
            for (int i = 3; i + 2 < indices.Count; i += 3)
                foreach (var (a, b) in new[] { (indices[i], indices[i + 1]), (indices[i + 1], indices[i + 2]), (indices[i + 2], indices[i]) })
                    expected.Add(a < b ? (a, b) : (b, a));
            Assert.Equal(expected.Count, DrawableScene.CountEdges(indices, 3, triangles * 3));
        }
        // A quad: two triangles, five edges.
        Assert.Equal(5, DrawableScene.CountEdges([0, 1, 2, 0, 2, 3], 0, 6));
    }

    [Fact]
    public void Every_field_of_every_vertex_is_read_as_the_layout_has_it_and_a_missing_one_as_its_default()
    {
        Vector3[] positions = [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, .5f)];
        var mesh = new DrawableMeshInput(positions, [0, 1, 2, 0, 2, 3], "surface");
        // One normal too short to be one, taken as straight up.
        mesh.SetChannel(VertexSemantic.Normal, [Vector4.UnitZ, new(0, .2f, 0, 0), Vector4.UnitX, Vector4.UnitY]);
        mesh.SetChannel(VertexSemantic.Tangent, [new(1, 0, 0, 1), new(0, 1, 0, -1), new(1, 0, 0, 1), new(0, 0, 1, -1)]);
        mesh.SetChannel(VertexSemantic.TexCoord0, [new(0, 0, 0, 0), new(1, 0, 0, 0), new(1, 1, 0, 0), new(.25f, .75f, 0, 0)]);
        mesh.SetChannel(VertexSemantic.TexCoord1, [new(.5f, .5f, 0, 0), new(.1f, .2f, 0, 0), new(.3f, .4f, 0, 0), new(.6f, .7f, 0, 0)]);
        mesh.SetChannel(VertexSemantic.Color0, [new(1, 0, 0, 1), new(0, 1, 0, .5f), new(0, 0, 1, 0), new(.2f, .4f, .6f, .8f)]);
        mesh.SetChannel(VertexSemantic.TexCoord2, [.. Enumerable.Repeat(new Vector4(.1f, .9f, 0, 0), 4)]);
        var builder = new DrawableBuilder { Name = "fields" };
        builder.Materials.Add(new DrawableMaterialInput { Name = "surface", Shader = "normal_spec" });
        var model = new DrawableModelInput();
        model.Meshes.Add(mesh);
        builder.Models.Add(model);
        byte[] ydr = new Ydr(builder.Build(GameTarget.Legacy), GameTarget.Legacy).Write();
        DrawableScene scene = ModelContent.Read("fields.ydr", ydr).Surfaces!;

        // What each vertex holds, read field by field.
        VertexBuffer buffer = Ydr.Read(ydr).Drawable.Lods[CommonFury.Assets.DrawableLod.High].Models[0].Geometries[0].Vertices;
        Assert.Equal(buffer.Count, scene.Vertices.Length);
        Vector4 Field(int i, VertexSemantic semantic, Vector4 missing) => buffer.Layout.TryFind(semantic, out _) ? buffer.ReadElement(i, semantic) : missing;
        Assert.False(buffer.Layout.TryFind(VertexSemantic.Color1, out _));
        for (int i = 0; i < buffer.Count; i++)
        {
            SurfaceVertex vertex = scene.Vertices[i];
            Assert.Equal(Field(i, VertexSemantic.Position, default).AsVector3() - scene.Origin, vertex.Position);
            Vector3 normal = Field(i, VertexSemantic.Normal, Vector4.UnitZ).AsVector3();
            Assert.Equal(normal.LengthSquared() < .1f ? Vector3.UnitZ : normal, vertex.Normal);
            Vector4 tangent = Field(i, VertexSemantic.Tangent, Vector4.UnitX + Vector4.UnitW);
            Assert.Equal(new Vector4(tangent.AsVector3(), tangent.W < 0 ? -1 : 1), vertex.Tangent);
            Assert.Equal(Field(i, VertexSemantic.TexCoord0, default).AsVector2(), vertex.Uv0);
            Assert.Equal(Field(i, VertexSemantic.TexCoord1, default).AsVector2(), vertex.Uv1);
            Assert.Equal(SurfaceVertex.Pack(Field(i, VertexSemantic.Color0, Vector4.One)), vertex.Colour0);
            // No second colour: nothing.
            Assert.Equal(SurfaceVertex.Pack(Vector4.Zero), vertex.Colour1);
            Assert.Equal(Field(i, VertexSemantic.TexCoord2, default).AsVector2(), vertex.Uv2);
        }
        Assert.Equal(Vector3.UnitZ, scene.Vertices[1].Normal);
    }
}
