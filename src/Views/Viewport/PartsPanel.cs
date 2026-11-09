using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using HikariHopper.Rendering;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>
/// The 3D viewer's parts panel: a drawable's drawables, models and geometries at the level of detail picked, each
/// with a box to show or hide it and its triangles; a box is partly filled while only some of what is under it shows.
/// </summary>
internal sealed class PartsPanel : UserControl
{
    private const double RowHeight = 22;
    private const double Indent = 14;

    private readonly StackPanel rows = new();
    private readonly TextBlock counts = new();
    private readonly HashSet<string> expanded = [];
    private PartVisibility? parts;

    public PartsPanel()
    {
        Background = Ui.Theme.AppBg;
        var title = new TextBlock
        {
            Text = "Parts",
            FontFamily = Ui.Theme.UiFont,
            FontSize = Ui.Theme.SmallFontSize,
            Foreground = Ui.Theme.TextDim,
            VerticalAlignment = VerticalAlignment.Center,
        };
        counts.FontFamily = Ui.Theme.MonoFont;
        counts.FontSize = Ui.Theme.SmallFontSize;
        counts.Foreground = Ui.Theme.TextFaint;
        counts.VerticalAlignment = VerticalAlignment.Center;
        counts.HorizontalAlignment = HorizontalAlignment.Right;
        var header = new Border
        {
            Height = 26,
            Background = Ui.Theme.ListHeaderBg,
            BorderBrush = Ui.Theme.BorderHard,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 0, 10, 0),
            Child = new Panel { Children = { title, counts } },
        };
        DockPanel.SetDock(header, Dock.Top);
        rows.Margin = new Thickness(0, 4, 0, 4);
        Content = new DockPanel
        {
            Children = { header, new ScrollViewer { Content = rows, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled } },
        };
    }

    /// <summary>Raised when the user shows or hides parts.</summary>
    public event Action? Changed;

    /// <summary>The parts shown, or none; the panel lists them, the top rows open.</summary>
    public PartVisibility? Parts
    {
        get => parts;
        set
        {
            parts = value;
            expanded.Clear();
            if (value is not null)
                foreach (PartNode node in value.Tree())
                    expanded.Add(node.Key);
            Refresh();
        }
    }

    /// <summary>The rows shown, in order, for tests: each a node and whether it is open.</summary>
    internal IReadOnlyList<(PartNode Node, bool Open)> Rows { get; private set; } = [];

    /// <summary>The header's count: geometries shown of those at this level.</summary>
    internal string CountText => counts.Text ?? "";

    /// <summary>Opens or closes a row.</summary>
    internal void Toggle(PartNode node)
    {
        if (!expanded.Remove(node.Key))
            expanded.Add(node.Key);
        Refresh();
    }

    /// <summary>Shows or hides what is under a row: a partly shown row is shown whole.</summary>
    internal void Flip(PartNode node)
    {
        if (parts is null)
            return;
        parts.Show(node, parts.State(node) != true);
        Refresh();
        Changed?.Invoke();
    }

    /// <summary>Lists the rows again, after the level or what is shown changed.</summary>
    public void Refresh()
    {
        rows.Children.Clear();
        var listed = new List<(PartNode, bool)>();
        if (parts is not null)
        {
            void Add(PartNode node)
            {
                bool open = node.Children.Count > 0 && expanded.Contains(node.Key);
                listed.Add((node, open));
                rows.Children.Add(Row(node, open));
                if (open)
                    foreach (PartNode child in node.Children)
                        Add(child);
            }
            foreach (PartNode node in parts.Tree())
                Add(node);
            (int shown, int total) = parts.Counts();
            counts.Text = $"{shown} / {total}";
        }
        else
        {
            counts.Text = "";
        }
        Rows = listed;
    }

    private Control Row(PartNode node, bool open)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"{6 + node.Depth * Indent},14,22,*,Auto"),
            Height = RowHeight,
            Background = Brushes.Transparent,
            Margin = new Thickness(4, 0, 4, 0),
        };
        if (node.Children.Count > 0)
        {
            var caret = new LucideIcon
            {
                IconName = open ? "chevron-down" : "chevron-right",
                Width = 10,
                Height = 10,
                Weight = 1.8,
                Stroke = Ui.Theme.TextFaint,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var hit = new Border { Background = Brushes.Transparent, Child = caret, Cursor = new Cursor(StandardCursorType.Hand) };
            hit.PointerPressed += (_, e) =>
            {
                Toggle(node);
                e.Handled = true;
            };
            Grid.SetColumn(hit, 1);
            grid.Children.Add(hit);
        }
        var box = new SquareCheckBox
        {
            Padding = new Thickness(0),
            IsThreeState = false,
            IsChecked = parts?.State(node),
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = false,
        };
        box.Click += (_, e) =>
        {
            Flip(node);
            e.Handled = true;
        };
        Grid.SetColumn(box, 2);
        grid.Children.Add(box);
        var name = new TextBlock
        {
            Text = node.Name,
            FontFamily = Ui.Theme.UiFont,
            FontSize = Ui.Theme.FontSize,
            Foreground = Ui.Theme.TextRow,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(name, 3);
        grid.Children.Add(name);
        var triangles = new TextBlock
        {
            Text = Compact(node.Triangles),
            FontFamily = Ui.Theme.MonoFont,
            FontSize = Ui.Theme.SmallFontSize,
            Foreground = Ui.Theme.TextFaint,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 6, 0),
        };
        Grid.SetColumn(triangles, 4);
        grid.Children.Add(triangles);
        ThemedToolTip.Set(grid, $"{node.Name}: {node.Triangles.ToString("N0", CultureInfo.InvariantCulture)} triangles");
        // Clicking the row opens or closes it, as its caret does.
        grid.PointerPressed += (_, e) =>
        {
            if (node.Children.Count > 0 && e.GetCurrentPoint(grid).Properties.IsLeftButtonPressed)
                Toggle(node);
        };
        var row = new Border { CornerRadius = Ui.Theme.ItemCorner, Child = grid, Background = Brushes.Transparent };
        row.PointerEntered += (_, _) => row.Background = Ui.Theme.HoverBg;
        row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
        return row;
    }

    /// <summary>A triangle count as the row shows it: 840, 9.4k, 38k, 1.2m.</summary>
    internal static string Compact(int value) => value switch
    {
        < 1000 => value.ToString(CultureInfo.InvariantCulture),
        < 10_000 => (value / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + "k",
        < 1_000_000 => (value / 1000).ToString(CultureInfo.InvariantCulture) + "k",
        _ => (value / 1_000_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "m",
    };
}
