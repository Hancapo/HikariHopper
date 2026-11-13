using System.Numerics;
using Avalonia;
using Avalonia.Headless.XUnit;
using FiveFury;
using FiveFury.Drawables;
using HikariHopper.Rendering;
using HikariHopper.ViewModels;

namespace HikariHopper.Tests;

public sealed class ShaderValuesTests
{
    private static DrawableMaterial Material(string shader = "normal_spec", GameTarget target = GameTarget.Legacy) =>
        new DrawableMaterialInput { Name = "surface", Shader = shader }.Build(target);

    // A triangle drawn with one shader entry, as a .ydr.
    internal static byte[] Triangle(string shader = "normal_spec", GameTarget target = GameTarget.Legacy)
    {
        var mesh = new DrawableMeshInput([new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], [0, 1, 2], "surface");
        var builder = new DrawableBuilder { Name = "tri" };
        builder.Materials.Add(new DrawableMaterialInput { Name = "surface", Shader = shader });
        var model = new DrawableModelInput();
        model.Meshes.Add(mesh);
        builder.Models.Add(model);
        return new FiveFury.Ydr.Ydr(builder.Build(target), target).Write();
    }

    [Fact]
    public void A_shader_entrys_values_are_listed_by_group_with_readable_names()
    {
        IReadOnlyList<ShaderValue> values = ShaderEdits.ValuesOf(Material());
        // Its numbers only, no textures, each named for what it does and grouped: specular before the surface's.
        ShaderValue intensity = Assert.Single(values, value => value.Name == "specularIntensityMult");
        Assert.Equal(("Intensity", "Specular", 1), (intensity.Label, intensity.Group, intensity.Components));
        Assert.Equal(("Bumpiness", "Surface"), values.Single(value => value.Name == "bumpiness") is var bump ? (bump.Label, bump.Group) : default);
        Assert.Equal(3, values.Single(value => value.Name == "specMapIntMask").Components);
        Assert.DoesNotContain(values, value => value.Name.Contains("Sampler", StringComparison.OrdinalIgnoreCase));
        string[] groups = [.. values.Select(value => value.Group).Distinct()];
        Assert.True(Array.IndexOf(groups, "Specular") < Array.IndexOf(groups, "Surface"));
        // One values.json doesn't name keeps its own, in words, under Other.
        Assert.Equal("Env eff tex tile UV", ShaderEdits.Words("envEffTexTileUV"));
        Assert.Equal("Use tessellation", ShaderEdits.Words("useTessellation"));
    }

    [Fact]
    public void A_change_is_made_in_the_entry_and_a_drag_is_undone_as_one_step()
    {
        DrawableMaterial material = Material();
        var edits = new ShaderEdits();
        int told = 0;
        edits.Changed += _ => told++;
        float[] first = ShaderEdits.Current(material, "specularIntensityMult");
        float[] Values(float intensity) => [intensity, .. first[1..]];

        // A drag: its first step, then two more joined to it.
        edits.Set(material, "specularIntensityMult", Values(.2f));
        edits.Set(material, "specularIntensityMult", Values(.3f), continuing: true);
        edits.Set(material, "specularIntensityMult", Values(.4f), continuing: true);
        edits.Close();
        Assert.Equal(.4f, ShaderEdits.Current(material, "specularIntensityMult")[0]);
        Assert.True(edits.IsChanged(material, "specularIntensityMult"));
        Assert.False(edits.IsChanged(material, "bumpiness"));
        Assert.True(edits.Dirty);
        Assert.Equal(3, told);
        // Another value is a step of its own.
        edits.Set(material, "bumpiness", [2, 0, 0, 0]);
        edits.Close();

        Assert.True(edits.Undo());
        Assert.Equal(1, ShaderEdits.Current(material, "bumpiness")[0]);
        Assert.True(edits.Undo());
        Assert.Equal(first, ShaderEdits.Current(material, "specularIntensityMult"));
        Assert.False(edits.Dirty);
        Assert.False(edits.Undo());
    }

    [Fact]
    public void A_reset_puts_the_files_value_back_and_reset_all_every_one_in_one_step()
    {
        DrawableMaterial material = Material();
        var edits = new ShaderEdits();
        float[] intensity = ShaderEdits.Current(material, "specularIntensityMult"), bump = ShaderEdits.Current(material, "bumpiness");
        edits.Set(material, "specularIntensityMult", [.5f, 0, 0, 0]);
        edits.Set(material, "bumpiness", [3, 0, 0, 0]);
        edits.Close();

        edits.Reset(material, "bumpiness");
        Assert.Equal(bump, ShaderEdits.Current(material, "bumpiness"));
        Assert.False(edits.IsChanged(material, "bumpiness"));
        // A reset is undone like any change.
        edits.Undo();
        Assert.Equal(3, ShaderEdits.Current(material, "bumpiness")[0]);

        edits.ResetAll(material);
        Assert.Equal(intensity, ShaderEdits.Current(material, "specularIntensityMult"));
        Assert.Equal(bump, ShaderEdits.Current(material, "bumpiness"));
        Assert.False(edits.Dirty);
        edits.Undo();
        Assert.Equal((.5f, 3f), (ShaderEdits.Current(material, "specularIntensityMult")[0], ShaderEdits.Current(material, "bumpiness")[0]));

        // Saved, the values as they are become the file's: what a reset goes back to.
        edits.MarkSaved();
        Assert.False(edits.Dirty);
        Assert.Equal(.5f, edits.Original(material, "specularIntensityMult")[0]);
    }

    [Fact]
    public void An_enhanced_entrys_values_are_edited_in_every_copy()
    {
        DrawableMaterial material = Material(target: GameTarget.Enhanced);
        ShaderValue intensity = Assert.Single(ShaderEdits.ValuesOf(material), value => value.Label == "Intensity" && value.Group == "Specular");
        var edits = new ShaderEdits();
        float[] values = ShaderEdits.Current(material, intensity.Name);
        values[0] = .75f;
        edits.Set(material, intensity.Name, values);
        Assert.Equal(.75f, ShaderEdits.Current(material, intensity.Name)[0]);
        Assert.All(material.Gen9Parameters!.Copies, copy => Assert.Contains(.75f, copy.ConstantBuffers.SelectMany(buffer => buffer.Floats.ToArray())));
    }

    [Fact]
    public void Changes_go_with_the_drawables_when_they_are_read_again()
    {
        DrawableMaterial before = Material(), after = Material();
        var edits = new ShaderEdits();
        edits.Set(before, "bumpiness", [2, 0, 0, 0]);
        edits.Close();
        Assert.Equal([0], edits.Carry([before], [after]));
        // The entry read again has the change, it counts as changed from the file, and undo takes it back there.
        Assert.Equal(2, ShaderEdits.Current(after, "bumpiness")[0]);
        Assert.True(edits.IsChanged(after, "bumpiness"));
        Assert.True(edits.Undo());
        Assert.Equal(1, ShaderEdits.Current(after, "bumpiness")[0]);
    }

    [Fact]
    public void A_save_marks_its_values_saved_on_drawables_read_again_while_it_ran()
    {
        DrawableMaterial before = Material(), after = Material();
        var edits = new ShaderEdits();
        edits.Set(before, "bumpiness", [2, 0, 0, 0]);
        var saved = edits.Snapshot();
        edits.Carry([before], [after]);
        edits.MarkSaved(saved);
        // The saved value is the file's on the entry read again: nothing to save, and a reset keeps it.
        Assert.False(edits.Dirty);
        Assert.Equal(2, edits.Original(after, "bumpiness")[0]);
        edits.Reset(after, "bumpiness");
        Assert.Equal(2, ShaderEdits.Current(after, "bumpiness")[0]);
    }

    [Fact]
    public void A_fields_drag_steps_by_a_hundredth_and_typing_keeps_a_number()
    {
        Assert.Equal(.01f, Views.ScrubField.Step(1, fine: false), 5);
        Assert.Equal(1f, Views.ScrubField.Step(100, fine: false), 5);
        Assert.Equal(.0001f, Views.ScrubField.Step(0, fine: true), 6);
        Assert.Equal("0.125", Views.ScrubField.Format(.125f));
        Assert.Equal("100", Views.ScrubField.Format(100));
    }

    [Fact]
    public void A_sliders_fill_spans_its_usual_range_and_a_segment_shortens_its_number()
    {
        // values.json's range where it gives one; else 0 to 1, or to twice the file's value, rounded up to a round number.
        Assert.Equal((0f, 500f), Assert.Single(ShaderEdits.ValuesOf(Material()), value => value.Name == "specularFalloffMult").Range);
        Assert.Equal((0f, 1f), Views.ShaderValuesCard.RangeOf(.3f));
        Assert.Equal((0f, 50f), Views.ShaderValuesCard.RangeOf(25));
        Assert.Equal((0f, 500f), Views.ShaderValuesCard.RangeOf(180));
        Assert.Equal((-5f, 5f), Views.ShaderValuesCard.RangeOf(-2));
        Assert.Equal(".23", Views.ScrubField.Short(.2314f));
        Assert.Equal("-.5", Views.ScrubField.Short(-.5f));
        Assert.Equal(".001", Views.ScrubField.Short(.001f));
        Assert.Equal("180", Views.ScrubField.Short(180));
        // Debug values aren't listed; colours are marked so.
        DrawableMaterial paint = Material("vehicle_paint3");
        Assert.True(Assert.Single(ShaderEdits.ValuesOf(paint), value => value.Name == "matDiffuseColor").Colour);
    }

    [AvaloniaFact]
    public void The_rarely_used_groups_start_folded_and_stay_as_left()
    {
        var card = new Views.ShaderValuesCard(new ShaderEdits());
        card.Show("door", Material("vehicle_paint3"));
        Assert.True(card.IsOpen("Specular"));
        Assert.False(card.IsOpen("Dirt"));
        // Opened, the next card opens it too.
        card.SetOpen("Dirt", true);
        card.Show("door", Material("vehicle_paint3"));
        Assert.True(card.IsOpen("Dirt"));
        card.SetOpen("Dirt", false);
    }

    [AvaloniaFact]
    public void A_vehicle_paint_slot_is_named_and_picked_from_its_list()
    {
        DrawableMaterial paint = Material("vehicle_paint3");
        var edits = new ShaderEdits();
        float[] values = ShaderEdits.Current(paint, "matDiffuseColor");
        (values[0], values[1], values[2]) = (2, 1, 1);
        edits.Set(paint, "matDiffuseColor", values);
        edits.MarkSaved();
        var card = new Views.ShaderValuesCard(edits);
        card.Show("door", paint);
        Assert.Equal("Primary", card.SlotShown("matDiffuseColor"));

        // Another slot, then a colour of its own: the slot well goes, the swatch and numbers come.
        ShaderValue colour = card.Values.Single(value => value.Name == "matDiffuseColor");
        card.PickSlot(colour, 3);
        Assert.Equal((2f, 3f, 3f), (ShaderEdits.Current(paint, "matDiffuseColor")[0], ShaderEdits.Current(paint, "matDiffuseColor")[1], ShaderEdits.Current(paint, "matDiffuseColor")[2]));
        Assert.Equal("Pearlescent", card.SlotShown("matDiffuseColor"));
        Assert.True(card.ResetShown("matDiffuseColor"));
        card.PickSlot(colour, null);
        Assert.Null(card.SlotShown("matDiffuseColor"));
        Assert.Equal(1f, card.Field("matDiffuseColor", 0)!.Value);
        Assert.True(edits.Undo());
        Assert.Equal("Pearlescent", card.SlotShown("matDiffuseColor"));
    }

    [AvaloniaFact]
    public void Typing_in_a_field_keeps_a_number_and_drops_anything_else()
    {
        var field = new Views.ScrubField { Value = 1 };
        var told = new List<float>();
        field.Edited += (value, _) => told.Add(value);
        field.StartTyping();
        field.Editor!.Text = "0,5";
        field.FinishTyping(keep: true);
        Assert.Equal([.5f], told);
        field.StartTyping();
        field.Editor!.Text = "soft";
        field.FinishTyping(keep: true);
        field.StartTyping();
        field.Editor!.Text = "2";
        field.FinishTyping(keep: false);
        Assert.Equal([.5f], told);
        Assert.False(field.Typing);
    }

    [AvaloniaFact]
    public void A_field_opened_and_left_as_shown_keeps_its_value_exactly()
    {
        // Shown to four decimals, these would change from a click.
        foreach (float exact in new[] { 1 / 3f, .03125f, .00001f })
        {
            var field = new Views.ScrubField { Value = exact };
            var told = new List<float>();
            field.Edited += (value, _) => told.Add(value);
            field.StartTyping();
            field.FinishTyping(keep: true);
            Assert.Empty(told);
            Assert.Equal(exact, field.Value);
        }

        // Typed over with the text it shows, it takes what was typed: 0.00001, shown as 0, typed as 0.
        var typed = new Views.ScrubField { Value = .00001f };
        var window = new Avalonia.Controls.Window { Width = 200, Height = 60, Content = typed };
        window.Show();
        var kept = new List<float>();
        typed.Edited += (value, _) => kept.Add(value);
        typed.StartTyping();
        TestWait.Pump(20);
        Assert.Equal("0", typed.Editor!.Text);
        Avalonia.Headless.HeadlessWindowExtensions.KeyTextInput(window, "0");
        Assert.Equal("0", typed.Editor!.Text);
        typed.FinishTyping(keep: true);
        Assert.Equal([0f], kept);
        window.Close();

        // On the card: a value saved with more decimals than it shows isn't changed by a click on it.
        Views.ModelViewer viewer = Open(Triangle());
        viewer.PickAt(new Vector3(.2f, .2f, 3), -Vector3.UnitZ);
        Assert.True(viewer.OpenShaderValues());
        Views.ScrubField bump = viewer.Card.Field("bumpiness")!;
        bump.StartTyping();
        bump.Editor!.Text = "0.33333334";
        bump.FinishTyping(keep: true);
        viewer.ShaderEdits.MarkSaved();
        bump.StartTyping();
        Assert.Equal("0.3333", bump.Editor!.Text);
        bump.FinishTyping(keep: true);
        Assert.False(viewer.ShaderEdits.Dirty);
        Assert.Equal(1 / 3f, ShaderEdits.Current(viewer.Card.Material!, "bumpiness")[0]);
        viewer.Close();
    }

    private static Views.ModelViewer Open(byte[] ydr)
    {
        var viewer = new Views.ModelViewer(new ModelFile("tri.ydr", ydr));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!viewer.Reading.IsCompleted && clock.ElapsedMilliseconds < 10_000)
            TestWait.Pump(10);
        return viewer;
    }

    [AvaloniaFact]
    public void A_picked_geometry_shows_its_values_and_a_drag_is_drawn_at_once()
    {
        Views.ModelViewer viewer = Open(Triangle());
        Assert.Null(viewer.Card.Material);
        Assert.False(viewer.CardOverlay.IsWanted);

        // Picked on the model: no card until it's asked for; M opens it, with its shader's values, under the drawable's name.
        Assert.True(viewer.PickAt(new Vector3(.2f, .2f, 3), -Vector3.UnitZ));
        Assert.False(viewer.CardOverlay.IsWanted);
        Assert.True(viewer.Shortcut('M'));
        Assert.True(viewer.ShaderValuesOpen);
        Assert.True(viewer.CardOverlay.IsWanted);
        Assert.Same(viewer.Surfaces!.Sources[0], viewer.Card.Material);
        Assert.Equal("tri", viewer.Card.Title);
        Assert.Contains("Specular", viewer.Card.Groups);
        Views.ScrubField intensity = viewer.Card.Field("specularIntensityMult")!;
        float before = intensity.Value;
        Assert.Equal(before, viewer.Surfaces.Materials[0].Uniforms[2]);

        // Dragged: the entry, the field and what is drawn (the material's specular intensity) follow, with a reset.
        intensity.BeginDrag();
        intensity.Drag(20, fine: false, continuing: false);
        intensity.Drag(40, fine: false, continuing: true);
        float after = intensity.Value;
        Assert.NotEqual(before, after);
        Assert.Equal(after, ShaderEdits.Current(viewer.Card.Material!, "specularIntensityMult")[0]);
        viewer.FlushEdits();
        Assert.Equal(after, viewer.Surfaces.Materials[0].Uniforms[2]);
        Assert.True(viewer.Card.ResetShown("specularIntensityMult"));
        Assert.True(viewer.Card.ResetAllShown);
        Assert.False(viewer.Card.ResetShown("bumpiness"));

        // Ctrl+Z takes the whole drag back, and the card and the drawing with it.
        Assert.True(viewer.Command('Z'));
        Assert.Equal(before, intensity.Value);
        Assert.Equal(before, viewer.Surfaces.Materials[0].Uniforms[2]);
        Assert.False(viewer.Card.ResetAllShown);

        // Its close mark closes the card; the pick stays. M opens it again, and closes it.
        viewer.Card.RequestClose();
        Assert.False(viewer.CardOverlay.IsWanted);
        Assert.Single(viewer.Parts.Selection);
        Assert.NotEmpty(viewer.Viewport.Picked);
        Assert.True(viewer.Shortcut('M'));
        Assert.True(viewer.CardOverlay.IsWanted);
        Assert.True(viewer.Shortcut('M'));
        Assert.False(viewer.CardOverlay.IsWanted);
        Assert.True(viewer.OpenShaderValues());

        // Off the model: the card is out of the way, but still open, and back with the next geometry picked.
        Assert.False(viewer.PickAt(new Vector3(9, 9, 3), -Vector3.UnitZ));
        Assert.False(viewer.CardOverlay.IsWanted);
        Assert.True(viewer.ShaderValuesOpen);
        Assert.False(viewer.OpenShaderValues());
        Assert.True(viewer.PickAt(new Vector3(.2f, .2f, 3), -Vector3.UnitZ));
        Assert.True(viewer.CardOverlay.IsWanted);

        // Escape in the parts panel closes the card, keeping the pick; again, it lets go of the pick.
        Assert.True(viewer.Parts.HandleKey(Avalonia.Input.Key.Escape, Avalonia.Input.KeyModifiers.None));
        Assert.False(viewer.ShaderValuesOpen);
        Assert.Single(viewer.Parts.Selection);
        Assert.True(viewer.Parts.HandleKey(Avalonia.Input.Key.Escape, Avalonia.Input.KeyModifiers.None));
        Assert.Empty(viewer.Parts.Selection);
        Assert.True(viewer.PickAt(new Vector3(.2f, .2f, 3), -Vector3.UnitZ));
        Assert.False(viewer.CardOverlay.IsWanted);
        Assert.True(viewer.OpenShaderValues());

        // Closed, it stays closed through any pick until it's asked for again.
        Assert.True(viewer.Shortcut('M'));
        Assert.False(viewer.ShaderValuesOpen);
        Assert.False(viewer.PickAt(new Vector3(9, 9, 3), -Vector3.UnitZ));
        Assert.True(viewer.PickAt(new Vector3(.2f, .2f, 3), -Vector3.UnitZ));
        Assert.False(viewer.CardOverlay.IsWanted);
        viewer.Close();
    }

    [AvaloniaFact]
    public void The_card_comes_out_of_the_picked_row_and_goes_to_the_top_left_without_the_parts_panel()
    {
        var viewer = new Views.ModelViewer(new ModelFile("tri.ydr", Triangle()));
        var window = new Avalonia.Controls.Window { Width = 1000, Height = 700, Content = viewer };
        window.Show();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!viewer.Reading.IsCompleted && clock.ElapsedMilliseconds < 10_000)
            TestWait.Pump(10);
        viewer.SetPartsShown(true);
        TestWait.Pump(300);
        viewer.PickAt(new Vector3(.2f, .2f, 3), -Vector3.UnitZ);
        Assert.True(viewer.OpenShaderValues());
        TestWait.Pump(100);

        // Out of the panel's edge, its notch at the row's middle.
        Avalonia.Controls.Control row = viewer.Parts.SelectedRow!;
        double middle = row.TranslatePoint(new Avalonia.Point(0, row.Bounds.Height / 2), viewer.Viewport)!.Value.Y;
        (double? left, double? top, double? notch) = viewer.CardPlace;
        Assert.Equal(0, left);
        Assert.NotNull(notch);
        Assert.Equal(middle, top!.Value + notch!.Value, 0);

        // With the panel closed there's no row to come from: the view's top left, no notch.
        viewer.SetPartsShown(false);
        TestWait.Pump(300);
        Assert.Equal((Ui.Theme.ShaderCardLeft, Ui.Theme.ToolStripInset, (double?)null), viewer.CardPlace);
        window.Close();
    }

    [AvaloniaFact]
    public void The_card_shows_first_where_it_goes_not_where_it_was_last()
    {
        var viewer = new Views.ModelViewer(new ModelFile("tri.ydr", Triangle()));
        var window = new Avalonia.Controls.Window { Width = 1000, Height = 700, Content = viewer };
        window.Show();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!viewer.Reading.IsCompleted && clock.ElapsedMilliseconds < 10_000)
            TestWait.Pump(10);
        viewer.SetPartsShown(true);
        TestWait.Pump(300);
        viewer.PickAt(new Vector3(.2f, .2f, 3), -Vector3.UnitZ);
        // First out of the picked row, then closed; with the panel gone its place is the view's top left.
        Assert.True(viewer.OpenShaderValues());
        TestWait.Pump(100);
        PixelPoint atRow = viewer.CardOverlay.Window!.Position;
        viewer.CloseShaderValues();
        viewer.SetPartsShown(false);
        TestWait.Pump(300);

        // Opened again: the position it shows at is already the top left one, not the row's it had.
        PixelPoint? shownAt = null;
        viewer.CardOverlay.Window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.IsVisibleProperty && viewer.CardOverlay.Window.IsVisible && shownAt is null)
                shownAt = viewer.CardOverlay.Window.Position;
        };
        Assert.True(viewer.OpenShaderValues());
        TestWait.Pump(100);
        Assert.Equal((Ui.Theme.ShaderCardLeft, Ui.Theme.ToolStripInset, (double?)null), viewer.CardPlace);
        Assert.NotEqual(atRow, viewer.CardOverlay.Window.Position);
        Assert.Equal(viewer.CardOverlay.Window.Position, shownAt);
        window.Close();
    }

    [AvaloniaFact]
    public void The_parts_menu_opens_the_picked_geometrys_shader_values_first()
    {
        var viewer = new Views.ModelViewer(new ModelFile("tri.ydr", Triangle()));
        var window = new Avalonia.Controls.Window { Width = 1000, Height = 700, Content = viewer };
        window.Show();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!viewer.Reading.IsCompleted && clock.ElapsedMilliseconds < 10_000)
            TestWait.Pump(10);
        Views.PartsPanel.MenuRowItem[] Items()
        {
            viewer.Parts.OpenMenu(viewer.Parts, new Point(20, 40));
            Assert.NotNull(viewer.Parts.Menu);
            return [.. Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(viewer.Parts.Menu).OfType<Views.PartsPanel.MenuRowItem>()];
        }

        // A geometry picked: the menu's first item, in a group of its own, opens its shader values.
        Assert.True(viewer.PickAt(new Vector3(.2f, .2f, 3), -Vector3.UnitZ));
        Views.PartsPanel.MenuRowItem[] items = Items();
        Assert.Equal(["Shader parameters…", "Show only", "Hide", "Show all", "Frame", "Copy name"], items.Select(item => item.Text));
        Assert.True(items[0].IsEnabled);
        // Its groups are parted as the app's other menus are.
        Assert.Equal(2, Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(viewer.Parts.Menu!).OfType<Views.Controls.RetroMenuSeparator>().Count());
        items[0].Trigger();
        Assert.Null(viewer.Parts.Menu);
        Assert.True(viewer.ShaderValuesOpen);
        Assert.True(viewer.CardOverlay.IsWanted);

        // The drawable's row picked, which has no one shader: the card is out of the way, and the item is there but off.
        PartNode geometry = Assert.Single(viewer.Parts.Selection);
        viewer.Parts.Select(viewer.Parts.Rows[0].Node);
        Assert.False(viewer.CardOverlay.IsWanted);
        items = Items();
        Assert.Equal("Shader parameters…", items[0].Text);
        Assert.False(items[0].IsEnabled);
        viewer.Parts.Menu?.Close();
        // Still open: the geometry picked again, the card is back without asking.
        Assert.True(viewer.ShaderValuesOpen);
        viewer.Parts.Reveal(node => node.Key == geometry.Key);
        Assert.True(viewer.CardOverlay.IsWanted);
        window.Close();
    }

    [AvaloniaFact]
    public void A_value_reset_on_the_card_puts_back_the_files()
    {
        Views.ModelViewer viewer = Open(Triangle());
        viewer.PickAt(new Vector3(.2f, .2f, 3), -Vector3.UnitZ);
        Assert.True(viewer.OpenShaderValues());
        Views.ScrubField bump = viewer.Card.Field("bumpiness")!;
        float before = bump.Value;
        bump.StartTyping();
        bump.Editor!.Text = "2.5";
        bump.FinishTyping(keep: true);
        Assert.Equal(2.5f, bump.Value);
        viewer.FlushEdits();
        Assert.Equal(2.5f, viewer.Surfaces!.Materials[0].Uniforms[3]);
        viewer.Card.ResetValue("bumpiness");
        viewer.FlushEdits();
        Assert.Equal(before, bump.Value);
        Assert.Equal(before, viewer.Surfaces.Materials[0].Uniforms[3]);
        Assert.False(viewer.ShaderEdits.Dirty);
        viewer.Close();
    }
}
