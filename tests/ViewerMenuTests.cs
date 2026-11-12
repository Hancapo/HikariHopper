using System.Numerics;
using Avalonia.Headless.XUnit;
using FiveFury;
using FiveFury.Drawables;
using HikariHopper.Rendering;
using HikariHopper.Tests.Textures;
using HikariHopper.ViewModels;

namespace HikariHopper.Tests;

public sealed class ViewerMenuTests
{
    // A triangle drawn with one shader entry, carrying a texture of its own, as a .ydr.
    private static byte[] Textured()
    {
        var mesh = new DrawableMeshInput([new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], [0, 1, 2], "surface");
        var builder = new DrawableBuilder { Name = "tri" };
        builder.Materials.Add(new DrawableMaterialInput { Name = "surface", Shader = "normal_spec" });
        builder.AdditionalTextures.Add(TextureTestKit.Red("paint"));
        var model = new DrawableModelInput();
        model.Meshes.Add(mesh);
        builder.Models.Add(model);
        return new FiveFury.Ydr.Ydr(builder.Build(GameTarget.Legacy), GameTarget.Legacy).Write();
    }

    private static Views.ModelViewer Open(byte[] ydr, SaveTarget? target = null)
    {
        var viewer = new Views.ModelViewer(new ModelFile("tri.ydr", ydr) { SaveTarget = target });
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!viewer.Reading.IsCompleted && clock.ElapsedMilliseconds < 10_000)
            ExplorerStateRenders.Pump(10);
        viewer.PickAt(new Vector3(.2f, .2f, 3), -Vector3.UnitZ);
        Assert.True(viewer.OpenShaderValues());
        return viewer;
    }

    private static void Wait(Task task)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted && clock.ElapsedMilliseconds < 10_000)
            ExplorerStateRenders.Pump(10);
        Assert.True(task.IsCompleted);
    }

    private static void Bump(Views.ModelViewer viewer, string value)
    {
        Views.ScrubField field = viewer.Card.Field("bumpiness")!;
        field.StartTyping();
        field.Editor!.Text = value;
        field.FinishTyping(keep: true);
        viewer.FlushEdits();
    }

    private static bool Enabled(Views.Controls.ViewerMenuRow menus, string title, string text) =>
        menus.ItemsOf(title).Single(item => item.Text == text).Enabled;

    [AvaloniaFact]
    public void The_3d_viewer_has_file_and_edit_menus_whose_items_say_when_they_can_be_used()
    {
        Views.ModelViewer viewer = Open(Textured(), new SaveTarget(_ => { }));
        Views.Controls.ViewerMenuRow menus = viewer.Menus;
        Assert.Equal(["File", "Edit"], menus.Titles);
        Assert.Equal(["Save", "Save as…", "-", "Embedded textures…", "Extract embedded textures…", "-", "Close"],
            menus.ItemsOf("File").Select(item => item.Text));
        Assert.Equal(["Undo", "Redo", "-", "Reset the picked shader's values", "Reset every shader value"],
            menus.ItemsOf("Edit").Select(item => item.Text));
        Assert.Equal("Ctrl+Shift+S", menus.ItemsOf("File").Single(item => item.Text == "Save as…").Shortcut);

        // Nothing changed: Save, Undo, Redo and the resets can't be used; Save as and Extract can.
        Assert.False(Enabled(menus, "File", "Save"));
        Assert.True(Enabled(menus, "File", "Save as…"));
        Assert.True(Enabled(menus, "File", "Extract embedded textures…"));
        Assert.False(Enabled(menus, "Edit", "Undo"));
        Assert.False(Enabled(menus, "Edit", "Reset every shader value"));

        Bump(viewer, "2");
        Assert.True(Enabled(menus, "File", "Save"));
        Assert.True(Enabled(menus, "Edit", "Undo"));
        Assert.True(Enabled(menus, "Edit", "Reset the picked shader's values"));
        Assert.True(menus.Run("Edit", "Undo"));
        Assert.Equal(1, ShaderEdits.Current(viewer.Card.Material!, "bumpiness")[0]);
        Assert.True(Enabled(menus, "Edit", "Redo"));
        Assert.True(menus.Run("Edit", "Redo"));
        Assert.Equal(2, ShaderEdits.Current(viewer.Card.Material!, "bumpiness")[0]);
        Assert.True(menus.Run("Edit", "Reset every shader value"));
        Assert.False(viewer.ShaderEdits.Dirty);
        Assert.False(menus.Run("File", "Save"));
    }

    [AvaloniaFact]
    public void Ctrl_y_and_ctrl_shift_z_redo_and_ctrl_w_closes()
    {
        Views.ModelViewer viewer = Open(Textured());
        Bump(viewer, "3");
        Assert.True(viewer.Command('Z'));
        Assert.True(viewer.Command('Y'));
        Assert.Equal(3, ShaderEdits.Current(viewer.Card.Material!, "bumpiness")[0]);
        Assert.True(viewer.Command('Z'));
        Assert.True(viewer.Command('Z', shift: true));
        Assert.Equal(3, ShaderEdits.Current(viewer.Card.Material!, "bumpiness")[0]);
        // A new change forgets what was undone.
        viewer.Command('Z');
        Bump(viewer, "4");
        Assert.False(viewer.Command('Y'));
        Assert.True(viewer.Command('W'));
    }

    [AvaloniaFact]
    public void Save_as_writes_the_drawable_as_it_is_to_a_file_which_save_then_saves_to()
    {
        using var folder = new TempFolder();
        Views.ModelViewer viewer = Open(Textured());
        var dialogs = new FakeDialogs { SaveFile = folder.File("copy") };
        viewer.Dialogs = dialogs;
        Bump(viewer, "2.5");

        // Picked without its extension: it gets the file's.
        Task<bool> saving = viewer.SaveAs();
        Wait(saving);
        Assert.True(saving.Result, viewer.StatusText.Text);
        string copy = folder.File("copy.ydr");
        Assert.Equal(2.5f, ShaderEdits.Current(FiveFury.Ydr.Ydr.Read(File.ReadAllBytes(copy)).Drawable.Materials[0], "bumpiness")[0]);
        Assert.Equal("Save tri.ydr as", Assert.Single(dialogs.Titles));
        Assert.Equal("copy.ydr", viewer.TitleText.Text);
        Assert.False(viewer.ShaderEdits.Dirty);

        // From now on Save saves there.
        Bump(viewer, "3");
        Assert.Equal("copy.ydr *", viewer.TitleText.Text);
        Assert.True(viewer.Save());
        Wait(viewer.Saving);
        Assert.Equal(3f, ShaderEdits.Current(FiveFury.Ydr.Ydr.Read(File.ReadAllBytes(copy)).Drawable.Materials[0], "bumpiness")[0]);
        // Its embedded textures are the file it was opened as's, which it no longer is.
        Assert.False(Enabled(viewer.Menus, "File", "Embedded textures…"));
    }

    [AvaloniaFact]
    public void Extract_embedded_textures_writes_them_as_dds_files()
    {
        using var folder = new TempFolder();
        Views.ModelViewer viewer = Open(Textured());
        viewer.Dialogs = new FakeDialogs { Folder = folder.Path };
        Task<bool> extracting = viewer.ExtractTextures();
        Wait(extracting);
        Assert.True(extracting.Result, viewer.StatusText.Text);
        Assert.Equal(["paint.dds"], Directory.GetFiles(folder.Path).Select(Path.GetFileName));
        Assert.Equal("Extracted 1 texture", viewer.StatusText.Text);

        // A drawable without textures of its own has none to extract.
        Views.ModelViewer bare = Open(ShaderValuesTests.Triangle());
        Assert.False(Enabled(bare.Menus, "File", "Extract embedded textures…"));
    }

    [Fact]
    public void A_split_geometry_is_redone_with_its_change()
    {
        Drawable drawable = FiveFury.Ydr.Ydr.Read(ShaderValuesTests.Triangle()).Drawable;
        List<DrawableMaterial> list = drawable.Materials;
        DrawableGeometry geometry = drawable.Lods[CommonFury.Assets.DrawableLod.High].Models[0].Geometries[0];
        var edits = new ShaderEdits();
        int rebuilt = 0;
        edits.StructureChanged += () => rebuilt++;
        DrawableMaterial copy = edits.Split(geometry, list, list[0]);
        edits.Set(copy, "bumpiness", [4, 0, 0, 0]);
        edits.Close();
        edits.Undo();
        Assert.Single(list);
        Assert.True(edits.Redo());
        Assert.Equal(2, list.Count);
        Assert.Equal((ushort)1, geometry.MaterialIndex);
        Assert.Equal(4, ShaderEdits.Current(copy, "bumpiness")[0]);
        Assert.True(edits.Dirty);
        Assert.Equal(2, rebuilt);
    }

    [AvaloniaFact]
    public void The_text_viewer_has_file_and_edit_menus()
    {
        var viewer = new Views.TextViewer(HikariHopper.Core.TextDocument.Load("notes.txt", "one\ntwo\n"u8.ToArray()));
        Assert.Equal(["File", "Edit"], viewer.Menus.Titles);
        Assert.Equal(["Save", "Save as…", "-", "Close"], viewer.Menus.ItemsOf("File").Select(item => item.Text));
        Assert.Equal(["Undo", "Redo", "-", "Cut", "Copy", "Paste", "Select all", "-", "Find…", "Find next", "Find previous"],
            viewer.Menus.ItemsOf("Edit").Select(item => item.Text));
        // Find next needs something to find.
        Assert.False(Enabled(viewer.Menus, "Edit", "Find next"));
        viewer.FindField.Text = "two";
        Assert.True(Enabled(viewer.Menus, "Edit", "Find next"));
    }
}
