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
