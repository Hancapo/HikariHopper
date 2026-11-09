using System.Numerics;
using CommonFury.Assets;
using FiveFury;
using FiveFury.Drawables;
using FiveFury.Ydr;
using HikariHopper.Rendering;

namespace HikariHopper.Tests;

public sealed class PartVisibilityTests
{
    // A drawable with two models at the high level (a quad in default, a triangle in normal_spec) and one at the
    // medium level (a triangle in default), and no low or very low level.
    internal static DrawableScene Scene()
    {
        var builder = new DrawableBuilder { Name = "sign" };
        builder.Materials.Add(new DrawableMaterialInput { Name = "a", Shader = "default" });
        builder.Materials.Add(new DrawableMaterialInput { Name = "b", Shader = "normal_spec" });
        DrawableMeshInput Mesh(string material, int triangles)
        {
            Vector3[] positions = [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0)];
            int[] indices = triangles == 2 ? [0, 1, 2, 0, 2, 3] : [0, 1, 2];
            var mesh = new DrawableMeshInput(triangles == 2 ? positions : positions[..3], indices, material);
            return mesh;
        }
        foreach (var (lod, material, triangles) in new[] { (DrawableLod.High, "a", 2), (DrawableLod.High, "b", 1), (DrawableLod.Medium, "a", 1) })
        {
            var model = new DrawableModelInput { Lod = lod };
            model.Meshes.Add(Mesh(material, triangles));
            builder.Models.Add(model);
        }
        byte[] ydr = new Ydr(builder.Build(GameTarget.Legacy), GameTarget.Legacy).Write();
        return ModelContent.Read("sign.ydr", ydr).Surfaces!;
    }

    [Fact]
    public void Every_level_is_read_and_the_most_detailed_shows_first()
    {
        DrawableScene scene = Scene();
        Assert.Equal([0, 0, 1], scene.Parts.Select(part => part.Lod));
        Assert.Equal([true, true, false], scene.Parts.Select(part => part.Primary));
        // The scene's counts are what shows at first: the high level's three triangles, seven vertices, eight edges.
        Assert.Equal((3, 7, 8), (scene.TriangleCount, scene.VertexCount, scene.EdgeCount));
        Assert.Equal([("sign", (string?)null)], scene.Models);

        var parts = new PartVisibility(scene);
        Assert.Equal([0, 1], parts.Levels);
        Assert.Equal([true, true, false], parts.Mask());
        Assert.Equal((3, 7, 8), parts.Stats());
    }

    [Fact]
    public void Picking_a_level_shows_its_parts_and_their_counts()
    {
        var parts = new PartVisibility(Scene()) { Lod = 1 };
        Assert.Equal([false, false, true], parts.Mask());
        Assert.Equal((1, 3, 3), parts.Stats());
        Assert.Equal(("High", "Medium", "Low", "Very low"), (PartVisibility.LevelName(0), PartVisibility.LevelName(1), PartVisibility.LevelName(2), PartVisibility.LevelName(3)));
    }

    [Fact]
    public void The_tree_lists_the_drawable_its_models_and_their_geometries_by_shader()
    {
        var parts = new PartVisibility(Scene());
        PartNode drawable = Assert.Single(parts.Tree());
        Assert.Equal(("sign", 3), (drawable.Name, drawable.Triangles));
        // Two models without a skeleton: by number, each with its geometry.
        Assert.Equal(["model 1", "model 2"], drawable.Children.Select(child => child.Name));
        Assert.Equal(["default", "normal_spec"], drawable.Children.Select(child => Assert.Single(child.Children).Name));
        Assert.Equal((2, 2), parts.Counts());
    }

    [Fact]
    public void Hiding_a_row_hides_what_is_under_it_and_its_parent_shows_partly()
    {
        var parts = new PartVisibility(Scene());
        PartNode drawable = parts.Tree()[0];
        PartNode second = drawable.Children[1];
        parts.Show(second, false);
        Assert.Equal([true, false, false], parts.Mask());
        Assert.False(parts.State(second));
        Assert.Null(parts.State(drawable));
        Assert.Equal((2, 4, 5), parts.Stats());
        Assert.Equal((1, 2), parts.Counts());

        parts.Show(drawable, true);
        Assert.True(parts.State(drawable));
        Assert.Equal([true, true, false], parts.Mask());
    }

    [Fact]
    public void A_drawable_without_the_level_picked_shows_the_closest_it_has()
    {
        // Low and very low aren't in the drawable: it keeps showing its coarsest, the medium level.
        var parts = new PartVisibility(Scene()) { Lod = 3 };
        Assert.Equal(1, parts.LevelOf(0));
        Assert.Equal([false, false, true], parts.Mask());
    }

    [AvaloniaFact]
    public void A_press_on_a_box_sets_the_rows_it_sweeps_over_the_same_way()
    {
        var panel = new Views.PartsPanel { Parts = new PartVisibility(Scene()) };
        // The drawable, open, and its two models.
        Assert.Equal(["sign", "model 1", "model 2"], panel.Rows.Select(row => row.Node.Name));

        // Pressed on a shown model, then swept down and back: both models hidden, each once.
        panel.BeginSweep(panel.Rows[1].Node);
        panel.SweepOver(2);
        panel.SweepOver(1);
        panel.EndSweep();
        Assert.Equal([false, false, false], panel.Parts!.Mask());
        Assert.False(panel.Parts.State(panel.Rows[0].Node));

        // Pressed on a hidden one: the sweep shows them.
        panel.BeginSweep(panel.Rows[2].Node);
        panel.SweepOver(1);
        panel.EndSweep();
        Assert.Equal([true, true, false], panel.Parts.Mask());

        // Once let go, passing over the rows changes nothing.
        panel.SweepOver(1);
        Assert.Equal([true, true, false], panel.Parts.Mask());
    }

    [Theory]
    [InlineData(840, "840")]
    [InlineData(9_412, "9.4k")]
    [InlineData(38_200, "38k")]
    [InlineData(1_240_000, "1.2m")]
    public void A_row_counts_triangles_compactly(int triangles, string text) => Assert.Equal(text, Views.PartsPanel.Compact(triangles));
}
