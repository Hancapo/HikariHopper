using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using HikariHopper.Rendering;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>
/// A vehicle's paint, in the dropdown its footer well opens: the model's colour combinations, as the game picks one
/// when it spawns the vehicle, and the six slots' colours, each of which opens the game's colours, by finish, to pick
/// another from.
/// </summary>
internal sealed class PaintPanel : Border
{
    private const double PanelWidth = 340;
    private const int TileColumns = 8;
    private const double TileSize = 38;
    private const int SwatchColumns = 12;
    private const double SwatchCell = 26;

    private static readonly IImmutableSolidColorBrush Edge = new ImmutableSolidColorBrush(Color.FromArgb(46, 255, 255, 255));
    private static readonly IImmutableSolidColorBrush Grey = new ImmutableSolidColorBrush(Color.FromRgb(150, 150, 150));

    private readonly CarPaints paints;
    private readonly int?[] indices;
    private string finish = "";

    public PaintPanel(CarPaints paints, IReadOnlyList<int?> indices)
    {
        this.paints = paints;
        this.indices = [.. Enumerable.Range(0, VehiclePaint.Slots.Length).Select(slot => slot < indices.Count ? indices[slot] : null)];
        Width = PanelWidth;
        Focusable = true;
        KeyDown += OnKeyDown;
        ShowSlots();
    }

    /// <summary>A combination or a colour was picked: the six slots' colour numbers now.</summary>
    public event Action<IReadOnlyList<int?>>? Changed;

    /// <summary>The six slots' colour numbers.</summary>
    public IReadOnlyList<int?> Indices => indices;

    /// <summary>The slot whose colours the panel shows to pick from; none while it shows the slots.</summary>
    public int? PickingSlot { get; private set; }

    /// <summary>The combination the slots are, as the game lists them; none for colours of one's own.</summary>
    public int? Combination
    {
        get
        {
            for (int i = 0; i < paints.Combinations.Count; i++)
                if (Enumerable.Range(0, indices.Length).All(slot => (slot < paints.Combinations[i].Length ? paints.Combinations[i][slot] : null) == indices[slot]))
                    return i;
            return null;
        }
    }

    /// <summary>Paints the vehicle in the model's <paramref name="combination"/>.</summary>
    public void PickCombination(int combination)
    {
        int?[] combo = paints.Combinations[combination];
        for (int slot = 0; slot < indices.Length; slot++)
            indices[slot] = slot < combo.Length ? combo[slot] : null;
        Changed?.Invoke(indices);
        ShowSlots();
    }

    /// <summary>
    /// Paints <paramref name="slot"/> in the colour numbered <paramref name="colour"/>; the colours stay open on the same
    /// finish, so another can be tried straight after.
    /// </summary>
    public void PickColour(int slot, int colour)
    {
        indices[slot] = colour;
        Changed?.Invoke(indices);
        if (PickingSlot == slot)
            Child = Picker(slot, paints.Colour(colour), paints.Finishes);
    }

    /// <summary>The finish whose colours the picker shows.</summary>
    public string Finish => finish;

    /// <summary>Shows the picker's colours of <paramref name="name"/>, one of the game's finishes.</summary>
    public void ShowFinish(string name)
    {
        if (PickingSlot is not { } slot || name == finish || !paints.Finishes.Contains(name))
            return;
        finish = name;
        Child = Picker(slot, paints.Colour(indices[slot]), paints.Finishes);
    }

    /// <summary>Shows the game's colours to pick <paramref name="slot"/>'s from, in the finish of the one it has.</summary>
    public void ShowPicker(int slot)
    {
        PickingSlot = slot;
        CarColour? current = paints.Colour(indices[slot]);
        IReadOnlyList<string> finishes = paints.Finishes;
        finish = current?.Finish is { } own && finishes.Contains(own) ? own : finishes.FirstOrDefault() ?? "";
        Child = Picker(slot, current, finishes);
    }

    /// <summary>Shows the combinations and the slots.</summary>
    public void ShowSlots()
    {
        PickingSlot = null;
        var body = new StackPanel { Margin = new Thickness(12, 10, 12, 8), Spacing = 8 };
        if (paints.Combinations.Count > 0)
        {
            var head = new DockPanel();
            var count = Small(Combination is { } at ? $"{at + 1} of {paints.Combinations.Count}" : "Your own", Ui.Theme.TextFaint);
            DockPanel.SetDock(count, Dock.Right);
            head.Children.Add(count);
            head.Children.Add(Caps("COMBINATIONS"));
            body.Children.Add(head);
            var tiles = new WrapPanel { ItemWidth = TileSize, ItemHeight = 30 };
            for (int i = 0; i < paints.Combinations.Count; i++)
                tiles.Children.Add(Tile(i, i == Combination));
            body.Children.Add(new Border
            {
                Background = Ui.Theme.InsetBg, CornerRadius = Ui.Theme.ControlCorner, Padding = new Thickness(3),
                HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Min(TileColumns, paints.Combinations.Count) * TileSize + 6, Child = tiles,
            });
            body.Children.Add(new RetroMenuSeparator { Margin = new Thickness(-12, 2) });
        }
        else
            body.Children.Add(Caps("PAINT"));
        var slots = new StackPanel { Margin = new Thickness(-10, 0) };
        for (int slot = 0; slot < VehiclePaint.Slots.Length; slot++)
            slots.Children.Add(SlotRow(slot));
        body.Children.Add(slots);
        Child = body;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Escape, Backspace or Left from the colours goes back to the slots; from the slots, Escape closes the dropdown.
        if (PickingSlot is not null && e.Key is Key.Escape or Key.Back or Key.Left)
        {
            ShowSlots();
            e.Handled = true;
        }
    }

    // A combination as the game picks it: the primary over most of the tile, the secondary below, the pearl between.
    private Control Tile(int combination, bool selected)
    {
        int?[] combo = paints.Combinations[combination];
        IBrush Fill(int slot) => slot < combo.Length && paints.Colour(combo[slot]) is { } colour ? Brush(colour) : Grey;
        var stripes = new Grid { RowDefinitions = new RowDefinitions("3*,1,2*"), Width = 30, Height = 22 };
        stripes.Children.Add(new Border { Background = Fill(0) });
        var pearl = new Border { Background = Fill(2) };
        Grid.SetRow(pearl, 1);
        stripes.Children.Add(pearl);
        var second = new Border { Background = Fill(1) };
        Grid.SetRow(second, 2);
        stripes.Children.Add(second);
        var tile = new Border
        {
            Padding = new Thickness(3), CornerRadius = Ui.Theme.ItemCorner, Cursor = new Cursor(StandardCursorType.Hand),
            Background = selected ? Ui.Theme.ChromeRaised : Brushes.Transparent,
            BorderBrush = selected ? Ui.Theme.Accent : Brushes.Transparent, BorderThickness = new Thickness(1),
            Child = new Border { CornerRadius = Ui.Theme.InnerCorner, ClipToBounds = true, BorderBrush = Edge, BorderThickness = new Thickness(1), Child = stripes },
        };
        string names = string.Join(", ", new[] { 0, 1, 2 }.Select(slot => slot < combo.Length ? paints.Colour(combo[slot])?.Name : null).OfType<string>().Distinct());
        Avalonia.Automation.AutomationProperties.SetName(tile, $"Combination {combination + 1}: {names}");
        ThemedToolTip.Set(tile, names);
        tile.PointerEntered += (_, _) => { if (!selected) tile.Background = Ui.Theme.HoverChrome; };
        tile.PointerExited += (_, _) => { if (!selected) tile.Background = Brushes.Transparent; };
        tile.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(tile).Properties.IsLeftButtonPressed)
            {
                PickCombination(combination);
                e.Handled = true;
            }
        };
        return tile;
    }

    private Control SlotRow(int slot)
    {
        CarColour? colour = paints.Colour(indices[slot]);
        var row = new DockPanel { Height = Ui.Theme.MenuItemHeight };
        var chevron = new LucideIcon { IconName = "chevron-right", Width = 10, Height = 10, Weight = 1.7, Stroke = Ui.Theme.TextDim, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(chevron, Dock.Right);
        row.Children.Add(chevron);
        var number = Small(colour?.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "", Ui.Theme.TextFaint);
        number.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(number, Dock.Right);
        row.Children.Add(number);
        var label = Text(VehiclePaint.Slots[slot], Ui.Theme.TextDim);
        label.Width = 86;
        DockPanel.SetDock(label, Dock.Left);
        row.Children.Add(label);
        Control swatch = Swatch(colour is null ? Grey : Brush(colour), 14);
        DockPanel.SetDock(swatch, Dock.Left);
        row.Children.Add(swatch);
        TextBlock name = colour is null ? Text("The game's grey", Ui.Theme.TextFaint) : Text(colour.Name, Ui.Theme.Text);
        name.Margin = new Thickness(8, 0, 0, 0);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        row.Children.Add(name);
        var item = new Border
        {
            CornerRadius = Ui.Theme.ItemCorner, Background = Brushes.Transparent, Padding = new Thickness(6, 0), Margin = new Thickness(4, 0),
            Cursor = new Cursor(StandardCursorType.Hand), Child = row,
        };
        Avalonia.Automation.AutomationProperties.SetName(item, $"{VehiclePaint.Slots[slot]}: {colour?.Name ?? "not set"}");
        item.PointerEntered += (_, _) => item.Background = Ui.Theme.HoverChrome;
        item.PointerExited += (_, _) => item.Background = Brushes.Transparent;
        item.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(item).Properties.IsLeftButtonPressed)
            {
                ShowPicker(slot);
                e.Handled = true;
            }
        };
        return item;
    }

    private Control Picker(int slot, CarColour? current, IReadOnlyList<string> finishes)
    {
        var body = new StackPanel { Margin = new Thickness(12, 10, 12, 10), Spacing = 10 };
        // Back to the slots, under the slot's name.
        var back = new Border
        {
            Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), HorizontalAlignment = HorizontalAlignment.Left,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6,
                Children = { new LucideIcon { IconName = "chevron-left", Width = 10, Height = 10, Weight = 1.7, Stroke = Ui.Theme.TextDim, VerticalAlignment = VerticalAlignment.Center }, Caps(VehiclePaint.Slots[slot].ToUpperInvariant()) },
            },
        };
        Avalonia.Automation.AutomationProperties.SetName(back, "Back to the slots");
        back.PointerPressed += (_, e) =>
        {
            ShowSlots();
            e.Handled = true;
        };
        body.Children.Add(back);
        // As tall as the finish with the most colours needs, so the picker keeps its size from one finish to another and
        // a short one leaves room in the well.
        int rows = Math.Max(1, finishes.Select(name => (paints.Colours.Count(colour => colour.Finish == name) + SwatchColumns - 1) / SwatchColumns).DefaultIfEmpty(1).Max());
        var swatches = new WrapPanel { ItemWidth = SwatchCell, ItemHeight = SwatchCell, Width = SwatchColumns * SwatchCell, Height = rows * SwatchCell };
        var hovered = new DockPanel { MinHeight = 16 };
        void Name(CarColour? colour)
        {
            hovered.Children.Clear();
            if (colour is null)
                return;
            var number = Small(colour.Index.ToString(System.Globalization.CultureInfo.InvariantCulture), Ui.Theme.TextFaint);
            DockPanel.SetDock(number, Dock.Right);
            hovered.Children.Add(number);
            hovered.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Swatch(Brush(colour), 14), Text(colour.Name, Ui.Theme.Text) } });
        }
        void Fill()
        {
            swatches.Children.Clear();
            foreach (CarColour colour in paints.Colours.Where(colour => colour.Finish == finish))
            {
                bool on = colour.Index == current?.Index;
                var cell = new Border
                {
                    Width = 24, Height = 24, Padding = new Thickness(2), CornerRadius = Ui.Theme.ItemCorner, Cursor = new Cursor(StandardCursorType.Hand),
                    BorderBrush = on ? Ui.Theme.Accent : Brushes.Transparent, BorderThickness = new Thickness(on ? 2 : 1),
                    Child = new Border { CornerRadius = Ui.Theme.InnerCorner, Background = Brush(colour), BorderBrush = Edge, BorderThickness = new Thickness(1) },
                };
                Avalonia.Automation.AutomationProperties.SetName(cell, colour.Name);
                cell.PointerEntered += (_, _) =>
                {
                    if (!on)
                        cell.BorderBrush = Ui.Theme.Border;
                    Name(colour);
                };
                cell.PointerExited += (_, _) =>
                {
                    if (!on)
                        cell.BorderBrush = Brushes.Transparent;
                    Name(current);
                };
                cell.PointerPressed += (_, e) =>
                {
                    if (e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed)
                    {
                        PickColour(slot, colour.Index);
                        e.Handled = true;
                    }
                };
                swatches.Children.Add(cell);
            }
        }
        if (finishes.Count > 1)
        {
            var switcher = new SegmentedSwitch { Height = Ui.Theme.ModelFooterControlHeight, HorizontalAlignment = HorizontalAlignment.Left };
            foreach (string name in finishes)
                switcher.Sides.Add(new SwitchSide { Text = name, Width = Math.Ceiling(name.Length * 6.6 + 16), Label = name, ToolTip = $"{name} colours" });
            switcher.SelectedIndex = Math.Max(0, finishes.ToList().IndexOf(finish));
            switcher.SideRequested += (_, side) =>
            {
                finish = finishes[side];
                switcher.SelectedIndex = side;
                Fill();
                Name(current);
            };
            body.Children.Add(switcher);
        }
        Fill();
        body.Children.Add(new Border { Background = Ui.Theme.InsetBg, CornerRadius = Ui.Theme.ControlCorner, Padding = new Thickness(5), HorizontalAlignment = HorizontalAlignment.Left, Child = swatches });
        Name(current);
        body.Children.Add(hovered);
        return body;
    }

    private static IBrush Brush(CarColour colour) =>
        new ImmutableSolidColorBrush(Color.FromRgb((byte)(colour.Argb >> 16), (byte)(colour.Argb >> 8), (byte)colour.Argb));

    private static Control Swatch(IBrush fill, double size) => new Border
    {
        Width = size, Height = size, CornerRadius = Ui.Theme.InnerCorner, Background = fill, BorderBrush = Edge, BorderThickness = new Thickness(1),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static TextBlock Text(string text, IBrush ink) => new()
    {
        Text = text, Foreground = ink, FontSize = Ui.Theme.FontSize, FontFamily = Ui.Theme.UiFont, VerticalAlignment = VerticalAlignment.Center,
    };

    private static TextBlock Small(string text, IBrush ink) => new()
    {
        Text = text, Foreground = ink, FontSize = Ui.Theme.SmallFontSize, FontFamily = Ui.Theme.MonoFont, VerticalAlignment = VerticalAlignment.Center,
    };

    private static TextBlock Caps(string text) => new()
    {
        Text = text, Foreground = Ui.Theme.TextDim, FontFamily = Ui.Theme.MonoFont, FontSize = Ui.Theme.SmallFontSize, FontWeight = FontWeight.Bold,
        LetterSpacing = 1.2, VerticalAlignment = VerticalAlignment.Center,
    };
}
