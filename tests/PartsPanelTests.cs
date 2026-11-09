using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using HikariHopper.Rendering;
using HikariHopper.Tests.Textures;

namespace HikariHopper.Tests;

/// <summary>The 3D viewer's parts panel: its rows, their selection, the keyboard, the filter and the menu.</summary>
public sealed class PartsPanelTests
{
    // The drawable, open, and its two models (each with a geometry under it, closed).
    private static Views.PartsPanel Panel() => new() { Parts = new PartVisibility(PartVisibilityTests.Scene()) };

    private static string[] Names(Views.PartsPanel panel) => [.. panel.Rows.Select(row => row.Node.Name)];

    [Fact]
    public void Each_node_says_what_it_is()
    {
        var parts = new PartVisibility(PartVisibilityTests.Scene());
        PartNode drawable = Assert.Single(parts.Tree());
        Assert.Equal(PartKind.Drawable, drawable.Kind);
        Assert.All(drawable.Children, child => Assert.Equal(PartKind.Child, child.Kind));
        Assert.All(drawable.Children.SelectMany(child => child.Children), geometry => Assert.Equal(PartKind.Geometry, geometry.Kind));
    }

    [Fact]
    public void Show_only_hides_the_rest_and_show_all_brings_everything_back()
    {
        var parts = new PartVisibility(PartVisibilityTests.Scene());
        PartNode drawable = parts.Tree()[0];
        parts.ShowOnly([drawable.Children[1]]);
        Assert.Equal([false, true, false], parts.Mask());
        parts.ShowAll();
        Assert.Equal([true, true, false], parts.Mask());
    }

    [Fact]
    public void The_bounds_of_rows_are_their_parts_triangles_in_the_world()
    {
        var parts = new PartVisibility(PartVisibilityTests.Scene());
        PartNode drawable = parts.Tree()[0];
        // The first model is a unit quad on the ground; the second a triangle within it.
        CommonFury.Numerics.Aabb3 quad = parts.Bounds([drawable.Children[0]])!.Value;
        Assert.Equal(new System.Numerics.Vector3(0, 0, 0), quad.Minimum);
        Assert.Equal(new System.Numerics.Vector3(1, 1, 0), quad.Maximum);
        Assert.Equal(quad, parts.Bounds([drawable])!.Value);
        Assert.Null(parts.Bounds([]));
    }

    [AvaloniaFact]
    public void A_click_selects_a_row_ctrl_adds_one_and_shift_takes_the_rows_between()
    {
        Views.PartsPanel panel = Panel();
        Assert.Equal(["sign", "model 1", "model 2"], Names(panel));
        panel.Select(panel.Rows[1].Node);
        Assert.Equal(["model 1"], panel.Selection.Select(node => node.Name));
        panel.Select(panel.Rows[0].Node, KeyModifiers.Control);
        Assert.Equal(["sign", "model 1"], panel.Selection.Select(node => node.Name));
        panel.Select(panel.Rows[0].Node, KeyModifiers.Control);
        Assert.Equal(["model 1"], panel.Selection.Select(node => node.Name));
        // As in Windows, a Ctrl+click moves where a Shift range starts, taking a row away or not: from sign to model 2.
        panel.Select(panel.Rows[2].Node, KeyModifiers.Shift);
        Assert.Equal(["sign", "model 1", "model 2"], panel.Selection.Select(node => node.Name));
        // From model 1, clicked, to model 2.
        panel.Select(panel.Rows[1].Node);
        panel.Select(panel.Rows[2].Node, KeyModifiers.Shift);
        Assert.Equal(["model 1", "model 2"], panel.Selection.Select(node => node.Name));
        // Selecting shows and hides nothing.
        Assert.Equal([true, true, false], panel.Parts!.Mask());
    }

    [AvaloniaFact]
    public void The_keyboard_moves_through_the_rows_opens_them_and_shows_hides_and_frames_what_is_selected()
    {
        Views.PartsPanel panel = Panel();
        IReadOnlyList<PartNode>? framed = null;
        int changes = 0;
        panel.FrameRequested += nodes => framed = nodes;
        panel.Changed += () => changes++;

        Assert.True(panel.HandleKey(Key.Down, KeyModifiers.None));
        Assert.Equal(["sign"], panel.Selection.Select(node => node.Name));
        panel.HandleKey(Key.Down, KeyModifiers.None);
        Assert.Equal(["model 1"], panel.Selection.Select(node => node.Name));
        // Right opens model 1; right again goes into it.
        panel.HandleKey(Key.Right, KeyModifiers.None);
        Assert.Equal(["sign", "model 1", "default", "model 2"], Names(panel));
        panel.HandleKey(Key.Right, KeyModifiers.None);
        Assert.Equal(["default"], panel.Selection.Select(node => node.Name));
        // Left goes back up to it, and left again closes it.
        panel.HandleKey(Key.Left, KeyModifiers.None);
        Assert.Equal(["model 1"], panel.Selection.Select(node => node.Name));
        panel.HandleKey(Key.Left, KeyModifiers.None);
        Assert.Equal(["sign", "model 1", "model 2"], Names(panel));

        // Shift+Down takes model 2 too; Space hides both, and again shows them.
        panel.HandleKey(Key.Down, KeyModifiers.Shift);
        Assert.Equal(["model 1", "model 2"], panel.Selection.Select(node => node.Name));
        panel.HandleKey(Key.Space, KeyModifiers.None);
        Assert.Equal([false, false, false], panel.Parts!.Mask());
        panel.HandleKey(Key.Space, KeyModifiers.None);
        Assert.Equal([true, true, false], panel.Parts.Mask());
        // Delete hides; Enter frames.
        panel.HandleKey(Key.Delete, KeyModifiers.None);
        Assert.Equal([false, false, false], panel.Parts.Mask());
        Assert.Equal(3, changes);
        panel.HandleKey(Key.Enter, KeyModifiers.None);
        Assert.Equal(["model 1", "model 2"], framed!.Select(node => node.Name));

        // Ctrl+A takes every row; Escape lets them all go, and is the viewer's again once nothing is selected.
        panel.HandleKey(Key.A, KeyModifiers.Control);
        Assert.Equal(3, panel.Selection.Count);
        Assert.True(panel.HandleKey(Key.Escape, KeyModifiers.None));
        Assert.Empty(panel.Selection);
        Assert.False(panel.HandleKey(Key.Escape, KeyModifiers.None));
        // Letters stay the viewer's shortcuts.
        Assert.False(panel.HandleKey(Key.B, KeyModifiers.None));
    }

    [AvaloniaFact]
    public void The_filter_keeps_the_rows_that_match_and_those_above_them_open()
    {
        Views.PartsPanel panel = Panel();
        panel.Filter.Text = "spec";
        // normal_spec is under model 2, under sign: both opened for it, model 1 gone.
        Assert.Equal(["sign", "model 2", "normal_spec"], Names(panel));
        panel.Filter.Text = "MODEL 1";
        Assert.Equal(["sign", "model 1"], Names(panel));
        panel.Filter.Text = "nothing like it";
        Assert.Empty(panel.Rows);
        Assert.Contains(panel.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == "No parts match");
        panel.Filter.Text = "";
        Assert.Equal(["sign", "model 1", "model 2"], Names(panel));
    }

    [AvaloniaFact]
    public void The_header_buttons_open_or_close_every_row_and_show_everything()
    {
        Views.PartsPanel panel = Panel();
        Assert.Equal("expand-all", panel.ExpandButton.IconKind);
        panel.ExpandButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(["sign", "model 1", "default", "model 2", "normal_spec"], Names(panel));
        Assert.Equal("collapse-all", panel.ExpandButton.IconKind);
        panel.ExpandButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(["sign"], Names(panel));

        panel.ShowOnly([panel.Rows[0].Node.Children[0]]);
        Assert.Equal([true, false, false], panel.Parts!.Mask());
        panel.ShowAllButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal([true, true, false], panel.Parts.Mask());
    }

    [AvaloniaFact]
    public void A_right_click_menu_acts_on_the_selection_and_copies_its_names()
    {
        var clipboard = new FakeClipboard();
        Infrastructure.IClipboardService previous = Infrastructure.AppServices.Clipboard;
        Infrastructure.AppServices.Clipboard = clipboard;
        var window = new Window { Width = 400, Height = 400 };
        try
        {
            Views.PartsPanel panel = Panel();
            window.Content = panel;
            window.Show();
            ExplorerStateRenders.Pump(100);
            panel.Select(panel.Rows[1].Node);
            panel.Select(panel.Rows[2].Node, KeyModifiers.Control);
            Views.PartsPanel.MenuRowItem Item(string text)
            {
                panel.OpenMenu(panel, new Point(20, 40));
                Assert.NotNull(panel.Menu);
                return panel.Menu.GetVisualDescendants().OfType<Views.PartsPanel.MenuRowItem>().Single(item => item.Text == text);
            }
            panel.OpenMenu(panel, new Point(20, 40));
            Assert.Equal(["Show only", "Hide", "Show all", "Frame", "Copy names"],
                panel.Menu!.GetVisualDescendants().OfType<Views.PartsPanel.MenuRowItem>().Select(item => item.Text));

            Item("Hide").Trigger();
            Assert.Null(panel.Menu);
            Assert.Equal([false, false, false], panel.Parts!.Mask());
            // Now they're hidden, the menu offers to show them.
            Item("Show").Trigger();
            Assert.Equal([true, true, false], panel.Parts.Mask());
            Item("Show only").Trigger();
            Assert.Equal([true, true, false], panel.Parts.Mask());
            Item("Copy names").Trigger();
            Assert.Equal($"model 1{Environment.NewLine}model 2", clipboard.Text);
        }
        finally
        {
            window.Close();
            Infrastructure.AppServices.Clipboard = previous;
        }
    }
}

/// <summary>The parts panel's sections over a drawable dictionary: one of each kind at a time, or all.</summary>
public sealed class PartsPanelVariantTests
{
    // A ped's drawables: two heads and a torso, each a triangle.
    private static DrawableScene Ped()
    {
        var dictionary = new FiveFury.Ydd.Ydd(FiveFury.GameTarget.Legacy);
        foreach (string name in new[] { "head_000_r", "head_001_r", "uppr_000_u" })
        {
            var builder = new FiveFury.Drawables.DrawableBuilder { Name = name };
            builder.Materials.Add(new FiveFury.Drawables.DrawableMaterialInput { Name = "skin", Shader = "ped" });
            var model = new FiveFury.Drawables.DrawableModelInput();
            model.Meshes.Add(new FiveFury.Drawables.DrawableMeshInput([new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], [0, 1, 2], "skin"));
            builder.Models.Add(model);
            dictionary.Drawables.Add(new FiveFury.Ydd.YddEntry(new CommonFury.Hashing.Hash32(name), builder.Build(FiveFury.GameTarget.Legacy)));
        }
        return ModelContent.Read("ped.ydd", dictionary.Write()).Surfaces!;
    }

    private static bool[] Shown(Views.PartsPanel panel) => panel.Parts!.Mask();

    [AvaloniaFact]
    public void A_drawable_dictionary_shows_one_drawable_of_each_kind_and_picking_one_shows_it_instead()
    {
        var panel = new Views.PartsPanel { Parts = new PartVisibility(Ped()) };
        Assert.Equal(["Head", "Torso"], panel.Sections.Select(section => section.Title));
        Assert.True(panel.ModeRow.IsVisible);
        Assert.True(panel.OneAtATime);
        Assert.Equal(["head_000_r", "head_001_r", "uppr_000_u"], panel.Rows.Select(row => row.Node.Name));
        // The first head and the torso.
        Assert.Equal([true, false, true], Shown(panel));

        // Clicking the second head shows it in place of the first; the torso stays.
        panel.Select(panel.Rows[1].Node);
        Assert.Equal([false, true, true], Shown(panel));
        // Up steps back to the first head.
        panel.HandleKey(Avalonia.Input.Key.Up, Avalonia.Input.KeyModifiers.None);
        Assert.Equal([true, false, true], Shown(panel));
        // Ctrl+click only selects.
        panel.Select(panel.Rows[1].Node, Avalonia.Input.KeyModifiers.Control);
        Assert.Equal([true, false, true], Shown(panel));
    }

    [AvaloniaFact]
    public void All_shows_every_drawable_and_one_brings_back_one_of_each()
    {
        var panel = new Views.PartsPanel { Parts = new PartVisibility(Ped()) };
        panel.Select(panel.Rows[1].Node);
        panel.SetOneAtATime(false);
        Assert.Equal(1, panel.ModeSwitch.SelectedIndex);
        Assert.Equal([true, true, true], Shown(panel));
        // In All, a click only selects.
        panel.Select(panel.Rows[0].Node);
        Assert.Equal([true, true, true], Shown(panel));

        // Back to one of each: the first shown of each kind.
        panel.SetOneAtATime(true);
        Assert.Equal([true, false, true], Shown(panel));
        // Show all, while one of each shows, switches to All.
        panel.ShowAllButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Assert.False(panel.OneAtATime);
        Assert.Equal([true, true, true], Shown(panel));
        // The drawables read again (their textures found, say): still All.
        panel.Parts = new PartVisibility(Ped());
        Assert.False(panel.OneAtATime);
        Assert.Equal([true, true, true], Shown(panel));
    }

    [AvaloniaFact]
    public void A_single_drawable_has_no_headings_and_no_switch()
    {
        var panel = new Views.PartsPanel { Parts = new PartVisibility(PartVisibilityTests.Scene()) };
        Assert.False(panel.ModeRow.IsVisible);
        PartSection section = Assert.Single(panel.Sections);
        Assert.Equal("", section.Title);
    }

    [AvaloniaFact]
    public void A_collisions_materials_are_rows_with_their_colour_to_show_hide_and_find()
    {
        // Two boxes of tarmac and one of another material, apart from each other.
        var composite = new FiveFury.Bounds.BoundComposite();
        FiveFury.Bounds.BoundBox Box(float x, byte material)
        {
            var box = new FiveFury.Bounds.BoundBox(new CommonFury.Numerics.Aabb3(new System.Numerics.Vector3(x, 0, 0), new System.Numerics.Vector3(x + 1, 1, 1)));
            box.Material.Type = (FiveFury.Bounds.BoundMaterialType)material;
            return box;
        }
        composite.Children.Add(new FiveFury.Bounds.BoundChild(Box(0, 4)));
        composite.Children.Add(new FiveFury.Bounds.BoundChild(Box(10, 1)));
        composite.Children.Add(new FiveFury.Bounds.BoundChild(Box(20, 4)));
        ViewportMesh mesh = CollisionMesh.Build(new FiveFury.Ybn.Ybn(composite));
        var marks = new MeshParts(mesh);
        var panel = new Views.PartsPanel { Parts = marks };

        // One section, the materials, the one most shapes wear first; each row a mark with its colour and its shapes.
        PartSection section = Assert.Single(panel.Sections);
        Assert.Equal((CollisionMesh.Section, PartSectionKind.Tree), (section.Title, section.Kind));
        string tarmac = CollisionMesh.Name((FiveFury.Bounds.BoundMaterialType)4), other = CollisionMesh.Name((FiveFury.Bounds.BoundMaterialType)1);
        Assert.Equal([tarmac, other], panel.Rows.Select(row => row.Node.Name));
        Assert.All(panel.Rows, row => Assert.Equal(PartKind.Mark, row.Node.Kind));
        Assert.Equal([2, 1], panel.Rows.Select(row => row.Node.Triangles));
        Assert.Equal(CollisionMesh.Colour((FiveFury.Bounds.BoundMaterialType)4), panel.Rows[0].Node.Colour);
        Assert.Equal("2 / 2", panel.CountText);
        Assert.False(panel.ModeRow.IsVisible);
        Assert.Equal(36 * 3, mesh.ToArray().Length);

        // Hidden, a material's triangles are left out of what is drawn; the rest stay.
        bool changed = false;
        panel.Changed += () => changed = true;
        panel.Flip(panel.Rows[0].Node);
        Assert.True(changed);
        Assert.Equal("1 / 2", panel.CountText);
        Assert.False(marks.State(panel.Rows[0].Node));
        ViewportVertex[] left = mesh.ToArray();
        Assert.Equal(12 * 3, left.Length);
        Assert.All(left, vertex => Assert.Equal(CollisionMesh.Colour((FiveFury.Bounds.BoundMaterialType)1), vertex.Colour));
        // The mesh itself keeps them, and the legend its counts.
        Assert.Equal(36, mesh.TriangleCount);
        Assert.Equal(2, mesh.Legend.Count);

        // Only this one, then all of them again.
        panel.ShowOnly([panel.Rows[0].Node]);
        Assert.Equal(24 * 3, mesh.ToArray().Length);
        Assert.False(marks.State(panel.Rows[1].Node));
        panel.ShowAll();
        Assert.Equal("2 / 2", panel.CountText);
        Assert.Equal(36 * 3, mesh.ToArray().Length);

        // A material is found by the box around what wears it: the other one's single box, in the world.
        CommonFury.Numerics.Aabb3 box = Assert.NotNull(marks.Bounds([panel.Rows[1].Node]));
        Assert.Equal(new System.Numerics.Vector3(10, 0, 0), box.Minimum);
        Assert.Equal(new System.Numerics.Vector3(11, 1, 1), box.Maximum);
        CommonFury.Numerics.Aabb3 both = Assert.NotNull(marks.Bounds([panel.Rows[0].Node]));
        Assert.Equal((0f, 21f), (both.Minimum.X, both.Maximum.X));

        // The filter narrows them by name, as it does parts.
        panel.Filter.Text = other;
        panel.Refresh();
        Assert.Equal([other], panel.Rows.Select(row => row.Node.Name));
    }
}

/// <summary>What the tests ask of a parts list that is a drawable's.</summary>
internal static class PartListTestExtensions
{
    /// <summary>A drawable's parts' flags for the renderer.</summary>
    public static bool[] Mask(this IPartList parts) => Assert.IsType<PartVisibility>(parts).Mask();
}
