using System.Numerics;
using HikariHopper.Rendering;
using HikariHopper.Rendering.World;

namespace HikariHopper.Tests;

public sealed class SceneGeometryTests
{
    private static SurfaceVertex At(float x) => new(new Vector3(x, 0, 0), Vector3.UnitZ, new Vector4(1, 0, 0, 1), default, default, 0, 0);

    [Fact]
    public void Sized_up_front_the_arrays_built_are_the_ones_returned()
    {
        var built = new SceneGeometry(3, 3);
        for (int v = 0; v < 3; v++)
            built.Add(At(v));
        ((ReadOnlySpan<uint>)[0, 1, 2]).CopyTo(built.AddIndices(3));
        SurfaceVertex[] vertices = built.Vertices();
        Assert.Same(vertices, built.Vertices());
        Assert.Equal([0u, 1, 2], built.AllIndices());
        // Past what was foreseen it grows, and gives back only what went in.
        var over = new SceneGeometry(1, 0);
        over.Add(At(0));
        over.Add(At(1));
        ((ReadOnlySpan<uint>)[1, 0]).CopyTo(over.AddIndices(2));
        Assert.Equal(2, over.Vertices().Length);
        Assert.Equal([1u, 0], over.AllIndices());
    }

    [Fact]
    public void A_part_counts_each_vertex_it_uses_once_and_finds_their_middle()
    {
        var built = new SceneGeometry(8, 12);
        // A geometry before, so the part's vertices start at 2.
        built.Add(At(100));
        built.Add(At(200));
        for (int v = 0; v < 4; v++)
            built.Add(At(v * 2));
        ((ReadOnlySpan<uint>)[0, 1, 2, 0, 2, 3, 3, 3, 3, 0, 0, 0]).CopyTo(built.AddIndices(12));
        Assert.Equal((4, new Vector3(3, 0, 0)), built.Used(0, 6, 2, 4));
        // Another part right after reuses the marks without counting the first part's.
        Assert.Equal((2, new Vector3(3, 0, 0)), built.Used(6, 6, 2, 4));
    }

    [Fact]
    public void A_world_layout_moves_its_scenes_vertices_into_the_assets_space_without_copying_them()
    {
        FiveFury.Drawables.Drawable drawable = FiveFury.Ydr.Ydr.Read(TestDrawables.Triangle()).Drawable;
        DrawableScene scene = DrawableScene.Build(drawable, []);
        SurfaceVertex[] own = scene.Vertices;
        Vector3[] relative = [.. own.Select(vertex => vertex.Position)];
        WorldLayout layout = WorldLayout.Of(scene);
        Assert.Same(own, layout.Vertices);
        Assert.Equal(relative.Select(position => position + scene.Origin), layout.Vertices.Select(vertex => vertex.Position));
    }
}
