using System.Numerics;
using Avalonia.Headless.XUnit;
using CommonFury.Assets;
using FiveFury;
using FiveFury.Drawables;
using HikariHopper.Rendering;
using HikariHopper.ViewModels;

namespace HikariHopper.Tests;

public sealed class ShaderApplyToTests
{
    // Two triangles side by side, each a model of its own, drawn with one shader entry: the left at 0..1, the right at 2..3.
    private static byte[] Pair()
    {
        var builder = new DrawableBuilder { Name = "pair" };
        builder.Materials.Add(new DrawableMaterialInput { Name = "surface", Shader = "normal_spec" });
        foreach (float x in new[] { 0f, 2f })
        {
            var model = new DrawableModelInput();
            model.Meshes.Add(new DrawableMeshInput([new(x, 0, 0), new(x + 1, 0, 0), new(x, 1, 0)], [0, 1, 2], "surface"));
            builder.Models.Add(model);
        }
        return new FiveFury.Ydr.Ydr(builder.Build(GameTarget.Legacy), GameTarget.Legacy).Write();
    }

    private static Views.ModelViewer Open(byte[] ydr, ModelSaveTarget? target = null)
    {
        var viewer = new Views.ModelViewer(new ModelFile("pair.ydr", ydr) { SaveTarget = target });
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!viewer.Reading.IsCompleted && clock.ElapsedMilliseconds < 10_000)
            ExplorerStateRenders.Pump(10);
        return viewer;
    }

    private static void PickLeft(Views.ModelViewer viewer) => Assert.True(viewer.PickAt(new Vector3(.2f, .2f, 3), -Vector3.UnitZ));

    private static void PickRight(Views.ModelViewer viewer) => Assert.True(viewer.PickAt(new Vector3(2.2f, .2f, 3), -Vector3.UnitZ));

    // The bumpiness each triangle is drawn with: its material's uniform.
    private static (float Left, float Right) Drawn(Views.ModelViewer viewer)
    {
        viewer.FlushEdits();
        DrawableScene scene = viewer.Surfaces!;
        float At(float x) => scene.Materials[scene.Parts[(int)new PartVisibility(scene).PartAt(new Vector3(x, .2f, 3), -Vector3.UnitZ)!].Material].Uniforms[3];
        return (At(.2f), At(2.2f));
    }

    private static void Type(Views.ScrubField field, string value)
    {
        field.StartTyping();
        field.Editor!.Text = value;
        field.FinishTyping(keep: true);
    }

    [AvaloniaFact]
    public void A_shared_entry_offers_all_or_this_one_and_all_is_the_default()
    {
        Views.ModelViewer viewer = Open(Pair());
        PickLeft(viewer);
        Assert.Equal(2, viewer.Card.Shared);
        Assert.True(viewer.Card.ApplyToShown);
        Assert.False(viewer.Card.ApplyToOne);

        // All: both triangles change.
        Type(viewer.Card.Field("bumpiness")!, "2");
        Assert.Equal((2f, 2f), Drawn(viewer));
        Assert.Single(viewer.Surfaces!.Sources);
    }

    [AvaloniaFact]
    public void This_one_gives_the_picked_geometry_an_entry_of_its_own_at_its_first_change()
    {
        Views.ModelViewer viewer = Open(Pair());
        PickLeft(viewer);
        DrawableMaterial shared = viewer.Card.Material!;
        viewer.Card.ApplyToOne = true;
        Views.ScrubField field = viewer.Card.Field("bumpiness")!;

        // A drag: its first step makes the copy, and the rest of it goes on in the same field, on the copy.
        field.BeginDrag();
        field.Drag(30, fine: false, continuing: false);
        Assert.Same(field, viewer.Card.Field("bumpiness"));
        field.Drag(60, fine: false, continuing: true);
        float dragged = field.Value;
        Assert.Equal(2, viewer.Surfaces!.Sources.Count);
        Assert.NotSame(shared, viewer.Card.Material);
        Assert.Equal((dragged, 1f), Drawn(viewer));
        Assert.Equal(1f, ShaderEdits.Current(shared, "bumpiness")[0]);
        // The copy is the left's alone: no Apply to, and the right still shares nothing with it.
        Assert.False(viewer.Card.ApplyToShown);
        Assert.True(viewer.ShaderEdits.Dirty);
        PickRight(viewer);
        Assert.Same(shared, viewer.Card.Material);
        Assert.Equal(1, viewer.Card.Shared);

        // One Ctrl+Z takes back the drag and the copy: the left shares the entry again.
        Assert.True(viewer.Command('Z'));
        Assert.Single(viewer.Surfaces.Sources);
        Assert.Equal((1f, 1f), Drawn(viewer));
        Assert.False(viewer.ShaderEdits.Dirty);
        PickLeft(viewer);
        Assert.Equal(2, viewer.Card.Shared);
    }

    [AvaloniaFact]
    public void A_geometry_given_an_entry_of_its_own_is_saved_with_it()
    {
        byte[]? written = null;
        Views.ModelViewer viewer = Open(Pair(), new ModelSaveTarget(bytes => written = bytes));
        PickRight(viewer);
        viewer.Card.ApplyToOne = true;
        Type(viewer.Card.Field("bumpiness")!, "3");
        Assert.True(viewer.Save());
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!viewer.Saving.IsCompleted && clock.ElapsedMilliseconds < 10_000)
            ExplorerStateRenders.Pump(10);
        Assert.True(viewer.Saving.Result, viewer.StatusText.Text);
        Assert.False(viewer.ShaderEdits.Dirty);

        // The file has two entries now, the right triangle drawn with the second, the left still with the first.
        Drawable drawable = FiveFury.Ydr.Ydr.Read(written!).Drawable;
        Assert.Equal(2, drawable.Materials.Count);
        Assert.Equal(1f, ShaderEdits.Current(drawable.Materials[0], "bumpiness")[0]);
        Assert.Equal(3f, ShaderEdits.Current(drawable.Materials[1], "bumpiness")[0]);
        var scene = DrawableScene.Build(drawable, []);
        Assert.Equal((1f, 3f), (At(scene, .2f), At(scene, 2.2f)));

        static float At(DrawableScene scene, float x) =>
            scene.Materials[scene.Parts[(int)new PartVisibility(scene).PartAt(new Vector3(x, .2f, 3), -Vector3.UnitZ)!].Material].Uniforms[3];
    }

    [AvaloniaFact]
    public void A_geometry_split_by_bone_says_what_else_it_is_drawn_on()
    {
        // A fragment's skinned body: one geometry, drawn as the chassis, the door and the extra.
        DrawableScene scene = FragmentViewerTests.DamagedScene();
        Assert.Same(scene.Geometries[0], scene.Geometries[1]);
        Assert.Equal(["chassis", "extra_1"], Views.ModelViewer.AlsoDrawnOn(scene, 1));
        var card = new Views.ShaderValuesCard(new ShaderEdits());
        card.SetAlsoOn(Views.ModelViewer.AlsoDrawnOn(scene, 1));
        Assert.Equal("Also drawn on chassis and extra_1", card.AlsoOn);
        card.SetAlsoOn(["a", "b", "c", "d"]);
        Assert.Equal("Also drawn on a, b and 2 more", card.AlsoOn);
        card.SetAlsoOn([]);
        Assert.Equal("", card.AlsoOn);
    }

    [Fact]
    public void A_split_is_undone_with_the_first_change_made_in_it()
    {
        Drawable drawable = FiveFury.Ydr.Ydr.Read(Pair()).Drawable;
        List<DrawableMaterial> list = drawable.Materials;
        DrawableMaterial material = list[0];
        DrawableGeometry geometry = drawable.Lods[DrawableLod.High].Models[0].Geometries[0];
        var edits = new ShaderEdits();
        int rebuilt = 0;
        edits.StructureChanged += () => rebuilt++;
        DrawableMaterial copy = edits.Split(geometry, list, material);
        Assert.Equal((2, (ushort)1), (list.Count, geometry.MaterialIndex));
        Assert.True(edits.Dirty);
        edits.Set(copy, "bumpiness", [4, 0, 0, 0]);
        edits.Close();
        Assert.True(edits.Undo());
        Assert.Equal((1, (ushort)0), (list.Count, geometry.MaterialIndex));
        Assert.Equal(1, rebuilt);
        Assert.False(edits.Dirty);
        Assert.False(edits.Undo());
    }
}
