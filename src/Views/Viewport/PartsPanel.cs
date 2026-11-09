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
    private readonly HashSet<string> swept = [];
    private PartVisibility? parts;
    // While a press on a box sweeps over the rows: what each is set to.
    private bool? sweepTo;

    public PartsPanel()
    {
        Background = Ui.Theme.AppBg;
        var title = new TextBlock
        {
            Text = "PARTS",
            FontFamily = Ui.Theme.MonoFont,
            FontSize = Ui.Theme.SmallFontSize,
            FontWeight = FontWeight.Bold,
            LetterSpacing = 1.4,
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
            Height = Ui.Theme.ListHeaderHeight,
            Background = Ui.Theme.ListHeaderBg,
            BorderBrush = Ui.Theme.BorderHard,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 0, 10, 0),
            Child = new Panel { Children = { title, counts } },
        };
        DockPanel.SetDock(header, Dock.Top);
        rows.Margin = new Thickness(0, 4, 0, 4);
        rows.Background = Brushes.Transparent;
        rows.PointerMoved += (_, e) =>
        {
            if (sweepTo is not null)
                SweepOver((int)Math.Floor(e.GetPosition(rows).Y / RowHeight));
        };
        rows.PointerReleased += (_, e) =>
        {
            e.Pointer.Capture(null);
            EndSweep();
        };
        rows.PointerCaptureLost += (_, _) => EndSweep();
        Content = new DockPanel
        {
            Children = { header, Scroller(rows) },
        };
    }

    // The app's quiet scroller, its bar over the rows while it's used.
    private static ScrollViewer Scroller(Control rows)
    {
        var scroller = new ScrollViewer { Content = rows, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        if (Application.Current?.TryFindResource("QuietOverlayScrollViewer", out object? theme) == true && theme is Avalonia.Styling.ControlTheme quiet)
            scroller.Theme = quiet;
        return scroller;
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

    /// <summary>
    /// A press on a row's box: it is shown, or hidden if it all showed, and the rows the pointer then passes over while
    /// held are set the same way.
    /// </summary>
    internal void BeginSweep(PartNode node)
    {
        if (parts is null)
            return;
        sweepTo = parts.State(node) != true;
        swept.Clear();
        Set(node);
    }

    /// <summary>The held pointer is over the row at <paramref name="index"/>: it is set as the sweep's first was.</summary>
    internal void SweepOver(int index)
    {
        if (sweepTo is not null && index >= 0 && index < Rows.Count)
            Set(Rows[index].Node);
    }

    /// <summary>The pointer let go: the sweep is over.</summary>
    internal void EndSweep() => sweepTo = null;

    private void Set(PartNode node)
    {
        if (parts is null || sweepTo is not { } to || !swept.Add(node.Key))
            return;
        if (parts.State(node) == to)
            return;
        parts.Show(node, to);
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
        // Handled before the box toggles itself: the panel sets it, and any the press then sweeps over.
        box.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(box).Properties.IsLeftButtonPressed)
                return;
            BeginSweep(node);
            e.Pointer.Capture(rows);
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
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
