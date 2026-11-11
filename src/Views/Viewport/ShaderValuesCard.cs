using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FiveFury.Drawables;
using HikariHopper.Rendering;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>
/// A number of a shader value, in a well: dragged sideways it changes as it goes (Shift for fine steps), and clicked it
/// opens for typing, Enter or leaving it to keep what was typed, Escape to drop it.
/// </summary>
internal sealed class ScrubField : Border
{
    private readonly TextBlock text;
    private TextBox? editor;
    private Point? pressedAt;
    private float pressedValue;
    private bool dragging;
    private float value;

    public ScrubField()
    {
        Height = 20;
        MinWidth = 40;
        CornerRadius = Ui.Theme.InnerCorner;
        Background = Ui.Theme.InsetBg;
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
        text = new TextBlock
        {
            FontFamily = Ui.Theme.MonoFont,
            FontSize = Ui.Theme.SmallFontSize,
            Foreground = Ui.Theme.TextRow,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Child = text;
        PointerEntered += (_, _) => Paint();
        PointerExited += (_, _) => Paint();
    }

    /// <summary>The number shown.</summary>
    public float Value
    {
        get => value;
        set
        {
            this.value = value;
            text.Text = Format(value);
        }
    }

    /// <summary>Whether it differs from the file's: its number in full ink.</summary>
    public bool Changed
    {
        set => text.Foreground = value ? Ui.Theme.Text : Ui.Theme.TextRow;
    }

    /// <summary>The number changed, by a drag or by typing; <c>Continuing</c> while a drag goes on after its first step.</summary>
    public event Action<float, bool>? Edited;

    /// <summary>A drag or a typed number ended.</summary>
    public event Action? Ended;

    /// <summary>The field opened for typing: the keyboard is needed in its window.</summary>
    public event Action? TypingStarted;

    /// <summary>Typing ended: the keyboard can go back.</summary>
    public event Action? TypingEnded;

    /// <summary>Whether it is open for typing.</summary>
    public bool Typing => editor is not null;

    /// <summary>A number as the card shows it: up to four decimals, no trailing zeros.</summary>
    internal static string Format(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>How much a pixel of drag changes a number: a hundredth of it, at least a thousandth; Shift a tenth of that.</summary>
    internal static float Step(float from, bool fine) => Math.Max(Math.Abs(from) * .01f, .001f) * (fine ? .1f : 1);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (editor is not null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        pressedAt = e.GetPosition(this);
        pressedValue = value;
        dragging = false;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (pressedAt is not { } from)
            return;
        double dx = e.GetPosition(this).X - from.X;
        if (!dragging && Math.Abs(dx) < 3)
            return;
        bool first = !dragging;
        dragging = true;
        Drag((float)dx, (e.KeyModifiers & KeyModifiers.Shift) != 0, !first);
    }

    /// <summary>Changes the number by a drag of <paramref name="dx"/> pixels from where it was pressed.</summary>
    internal void Drag(float dx, bool fine, bool continuing)
    {
        float next = pressedValue + dx * Step(pressedValue, fine);
        // Rounded to what the field shows, so a drag back lands where it started.
        next = float.Parse(Format(next), CultureInfo.InvariantCulture);
        Value = next;
        Edited?.Invoke(next, continuing);
    }

    /// <summary>Starts a drag from the number shown, as a press does.</summary>
    internal void BeginDrag() => pressedValue = value;

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (pressedAt is null)
            return;
        pressedAt = null;
        e.Pointer.Capture(null);
        if (dragging)
        {
            dragging = false;
            Ended?.Invoke();
        }
        else
            StartTyping();
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (pressedAt is null)
            return;
        pressedAt = null;
        if (dragging)
        {
            dragging = false;
            Ended?.Invoke();
        }
    }

    /// <summary>Opens the number for typing, all of it selected.</summary>
    internal void StartTyping()
    {
        if (editor is not null)
            return;
        editor = new TextBox
        {
            Text = Format(value),
            FontFamily = Ui.Theme.MonoFont,
            FontSize = Ui.Theme.SmallFontSize,
            Foreground = Ui.Theme.Text,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 0),
            MinHeight = 0,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        editor.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Escape)
            {
                FinishTyping(e.Key == Key.Enter);
                e.Handled = true;
            }
        };
        editor.LostFocus += (_, _) => FinishTyping(true);
        Child = editor;
        Cursor = null;
        Paint();
        TypingStarted?.Invoke();
        editor.Focus();
        editor.SelectAll();
    }

    /// <summary>Closes the field, keeping the number typed when <paramref name="keep"/> and it is one.</summary>
    internal void FinishTyping(bool keep)
    {
        if (editor is not { } open)
            return;
        editor = null;
        string typed = (open.Text ?? "").Trim().Replace(',', '.');
        Child = text;
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
        Paint();
        if (keep && float.TryParse(typed, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) && float.IsFinite(parsed)
            && parsed != value)
        {
            Value = parsed;
            Edited?.Invoke(parsed, false);
        }
        Ended?.Invoke();
        TypingEnded?.Invoke();
    }

    /// <summary>Types into the open field, as the keyboard does: for tests.</summary>
    internal TextBox? Editor => editor;

    private static readonly IBrush HoverWell = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Ui.Theme.Lighter(Ui.Theme.InsetBg.Color, 1.35));

    // The well brightens under the pointer, as the app's fields do, and takes the accent's border while typing.
    private void Paint()
    {
        Background = editor is not null || IsPointerOver ? HoverWell : Ui.Theme.InsetBg;
        BorderBrush = editor is not null ? Ui.Theme.BorderAccent : null;
        BorderThickness = new Thickness(editor is not null ? 1 : 0);
    }
}

/// <summary>
/// The values of the shader entry a picked geometry is drawn with, in a card over the view's top left: grouped, with
/// readable names, each number a field to drag or type, applied as it changes. A value that differs from the file's
/// has a reset beside it, and Reset all puts back every one. Textures and the shader itself aren't edited here.
/// </summary>
internal sealed class ShaderValuesCard : Border
{
    private readonly ShaderEdits edits;
    private readonly TextBlock title;
    private readonly TextBlock shader;
    private readonly StackPanel groups;
    private readonly ScrollViewer scroller;
    private readonly ChromeToolButton resetAll;
    private readonly List<Row> rows = [];
    // On a shared entry: whether a change goes to every geometry drawn with it, or to the picked one alone.
    private readonly SegmentedSwitch applyTo;
    private readonly DockPanel applyRow;
    private int shared = 1;
    // What else the picked geometry is drawn on: a geometry split by bone is a part under each.
    private readonly TextBlock alsoOn;

    // A value's row: its fields, one per number, and its reset.
    private sealed record Row(ShaderValue Value, ScrubField[] Fields, ChromeToolButton Reset);

    public ShaderValuesCard(ShaderEdits edits)
    {
        this.edits = edits;
        Width = CardWidth;
        Background = Ui.Theme.ChromeBg;
        BorderBrush = Ui.Theme.BorderHard;
        BorderThickness = new Thickness(1);
        CornerRadius = Ui.Theme.PanelCorner;
        Padding = new Thickness(10, 8, 10, 10);
        title = new TextBlock { FontSize = Ui.Theme.FontSize, Foreground = Ui.Theme.Text, TextTrimming = TextTrimming.CharacterEllipsis };
        shader = new TextBlock
        {
            FontFamily = Ui.Theme.MonoFont,
            FontSize = Ui.Theme.SmallFontSize,
            Foreground = Ui.Theme.TextFaint,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 2, 0, 0),
        };
        // Kept clear of the scroll bar, which shows over its right edge.
        groups = new StackPanel { Spacing = 3, Margin = new Thickness(0, 0, FieldsGutter, 0) };
        scroller = new ScrollViewer
        {
            Content = groups,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 4, 0, 0),
        };
        resetAll = new ChromeToolButton
        {
            Text = "Reset all",
            Height = 22,
            Width = 76,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
            CornerRadius = Ui.Theme.InnerCorner,
        };
        resetAll.Click += (_, _) =>
        {
            if (Material is { } material)
                edits.ResetAll(material);
        };
        Avalonia.Automation.AutomationProperties.SetName(resetAll, "Reset all the values");
        ThemedToolTip.Set(resetAll, "Put every value back as the file has it");
        applyTo = new SegmentedSwitch { Height = 22, VerticalAlignment = VerticalAlignment.Center };
        applyTo.Sides.Add(new SwitchSide { Text = "All", Width = 64, Label = "All the geometries drawn with it" });
        applyTo.Sides.Add(new SwitchSide { Text = "This one", Width = 72, Label = "This geometry alone" });
        applyTo.RefreshSides();
        applyTo.SideRequested += (_, side) => applyTo.SelectedIndex = side;
        Avalonia.Automation.AutomationProperties.SetName(applyTo, "Apply to");
        var applyLabel = new TextBlock { Text = "Apply to", FontSize = Ui.Theme.FontSize, Foreground = Ui.Theme.TextDim, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(applyTo, Dock.Right);
        applyRow = new DockPanel { Margin = new Thickness(0, 8, FieldsGutter, 0), IsVisible = false, Children = { applyTo, applyLabel } };
        alsoOn = new TextBlock
        {
            FontSize = Ui.Theme.SmallFontSize,
            Foreground = Ui.Theme.TextFaint,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, FieldsGutter, 0),
            IsVisible = false,
        };
        var header = new StackPanel { Children = { title, shader, alsoOn, applyRow } };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(resetAll, Dock.Bottom);
        Child = new DockPanel { Children = { header, resetAll, scroller } };
        Avalonia.Automation.AutomationProperties.SetName(this, "Shader values");
        edits.Changed += material =>
        {
            if (material == Material)
                Refresh();
        };
        // Saved, its values are the file's: no resets.
        edits.Saved += Refresh;
    }

    // The card's width, and what of it the rows have: less its border, its padding and the scroll bar's gutter.
    private const double CardWidth = 264, FieldsGutter = 8;
    private const double RowWidth = CardWidth - 2 - 20 - FieldsGutter;

    /// <summary>The shader entry shown; none while nothing is picked.</summary>
    public DrawableMaterial? Material { get; private set; }

    /// <summary>The values listed, in order.</summary>
    internal IReadOnlyList<ShaderValue> Values => [.. rows.Select(row => row.Value)];

    /// <summary>The group headings, in order.</summary>
    internal IReadOnlyList<string> Groups => [.. groups.Children.OfType<TextBlock>().Select(heading => heading.Text ?? "")];

    /// <summary>Whether Reset all is offered: a value differs from the file's.</summary>
    internal bool ResetAllShown => resetAll.IsVisible;

    /// <summary>The title shown: the geometry's name.</summary>
    internal string Title => title.Text ?? "";

    /// <summary>The field of one number of a value; none when it isn't listed.</summary>
    internal ScrubField? Field(string name, int component = 0) =>
        rows.FirstOrDefault(row => row.Value.Name == name)?.Fields.ElementAtOrDefault(component);

    /// <summary>Whether a value has its reset shown, as one that differs from the file's.</summary>
    internal bool ResetShown(string name) => rows.FirstOrDefault(row => row.Value.Name == name)?.Reset.IsVisible == true;

    /// <summary>Resets one value, as its reset does.</summary>
    internal void ResetValue(string name)
    {
        if (Material is { } material && rows.Any(row => row.Value.Name == name))
            edits.Reset(material, name);
    }

    /// <summary>
    /// Asked, at the first change in This one, to give the picked geometry an entry of its own: the viewer copies the
    /// entry, hands it over with <see cref="Adopt"/>, and the change goes to the copy.
    /// </summary>
    public Action? SplitRequested { get; set; }

    /// <summary>How many geometries are drawn with the entry shown: Apply to is offered when more than one.</summary>
    internal int Shared => shared;

    /// <summary>What the card says the geometry is also drawn on; empty when only on the part picked.</summary>
    internal string AlsoOn => alsoOn.IsVisible ? alsoOn.Text ?? "" : "";

    /// <summary>
    /// Says what else the picked geometry is drawn on, the parts its bones split it into: a change to it, This one
    /// included, shows on them too.
    /// </summary>
    public void SetAlsoOn(IReadOnlyList<string> parts)
    {
        alsoOn.IsVisible = parts.Count > 0;
        alsoOn.Text = parts.Count switch
        {
            0 => "",
            1 => $"Its geometry is also drawn on {parts[0]}.",
            2 => $"Its geometry is also drawn on {parts[0]} and {parts[1]}.",
            3 => $"Its geometry is also drawn on {parts[0]}, {parts[1]} and {parts[2]}.",
            _ => $"Its geometry is also drawn on {parts[0]}, {parts[1]} and {parts.Count - 2} more parts.",
        };
    }

    /// <summary>Whether Apply to is offered, the entry being shared.</summary>
    internal bool ApplyToShown => applyRow.IsVisible;

    /// <summary>Whether changes go to the picked geometry alone (This one), on a shared entry.</summary>
    internal bool ApplyToOne
    {
        get => shared > 1 && applyTo.SelectedIndex == 1;
        set => applyTo.SelectedIndex = value ? 1 : 0;
    }

    /// <summary>How many geometries are drawn with the entry shown, as the scene now counts them.</summary>
    public void SetShared(int count)
    {
        shared = Math.Max(1, count);
        applyTo.Sides[0].Text = $"All {shared}";
        applyTo.Sides[0].Label = $"All {shared} geometries drawn with it";
        applyTo.RefreshSides();
        applyRow.IsVisible = shared > 1;
    }

    /// <summary>Takes the picked geometry's own copy of the entry shown, which its changes go to from now on.</summary>
    public void Adopt(DrawableMaterial copy)
    {
        Material = copy;
        SetShared(1);
    }

    /// <summary>A field opened for typing, and needs the keyboard in the card's window.</summary>
    public event Action? TypingStarted;

    /// <summary>Typing ended: the keyboard goes back to the viewer.</summary>
    public event Action? TypingEnded;

    /// <summary>The tallest the card grows; it scrolls past that.</summary>
    public double Room
    {
        set => scroller.MaxHeight = Math.Max(80, value - 64);
    }

    /// <summary>
    /// Shows a shader entry's values, under <paramref name="name"/> (what the geometry belongs to), or nothing; drawn
    /// with it are <paramref name="geometries"/> geometries, and a change goes to all of them until This one is picked.
    /// </summary>
    public void Show(string name, DrawableMaterial? material, int geometries = 1)
    {
        applyTo.SelectedIndex = 0;
        SetShared(geometries);
        // A field being typed in keeps what was typed, for the entry it was.
        foreach (Row row in rows)
            foreach (ScrubField field in row.Fields)
                field.FinishTyping(true);
        Material = material;
        rows.Clear();
        groups.Children.Clear();
        if (material is null)
            return;
        title.Text = name;
        ShaderDefinition? definition = (material.Gen9Parameters is not null ? ShaderCatalog.Enhanced : ShaderCatalog.Legacy).Find(material.ShaderHash);
        shader.Text = definition?.Name ?? $"0x{material.ShaderHash.Value:X8}";
        string? group = null;
        foreach (ShaderValue value in ShaderEdits.ValuesOf(material))
        {
            if (value.Group != group)
            {
                group = value.Group;
                groups.Children.Add(new TextBlock
                {
                    Text = group.ToUpperInvariant(),
                    FontSize = Ui.Theme.CapsFontSize,
                    Foreground = Ui.Theme.TextFaint,
                    Margin = new Thickness(0, groups.Children.Count == 0 ? 2 : 8, 0, 1),
                });
            }
            groups.Children.Add(Build(material, value));
        }
        if (rows.Count == 0)
            groups.Children.Add(new TextBlock
            {
                Text = "This shader has no values to edit.",
                FontSize = Ui.Theme.SmallFontSize,
                Foreground = Ui.Theme.TextDim,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
            });
        Refresh();
    }

    // A value's row: its label and reset, its fields beside them (one or two numbers) or under them (more).
    private Control Build(DrawableMaterial material, ShaderValue value)
    {
        var label = new TextBlock
        {
            Text = value.Label,
            FontSize = Ui.Theme.FontSize,
            Foreground = Ui.Theme.TextRow,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        ThemedToolTip.Set(label, value.Name);
        var reset = new ChromeToolButton
        {
            Width = 18,
            Height = 18,
            Bordered = false,
            IconKind = "reset",
            IconSize = 11,
            IconThickness = 1.6,
            CornerRadius = Ui.Theme.InnerCorner,
            VerticalAlignment = VerticalAlignment.Center,
        };
        reset.Click += (_, _) => edits.Reset(Material ?? material, value.Name);
        ThemedToolTip.Set(reset, "Put it back as the file has it");
        Avalonia.Automation.AutomationProperties.SetName(reset, $"Reset {value.Label}");
        var fields = new ScrubField[value.Components];
        bool below = value.Components > 2;
        var line = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = below ? HorizontalAlignment.Stretch : HorizontalAlignment.Right };
        // Four to a line under the label, or one or two beside it.
        double width = below ? Math.Floor((RowWidth - 3 * 3) / 4) : value.Components == 1 ? 64 : 44;
        for (int i = 0; i < fields.Length; i++)
        {
            int component = i;
            var field = new ScrubField { Width = width, Margin = new Thickness(i % 4 == 0 ? 0 : 3, i >= 4 ? 3 : 0, 0, 0) };
            Avalonia.Automation.AutomationProperties.SetName(field, fields.Length == 1 ? value.Label : $"{value.Label} {component + 1}");
            field.Edited += (number, continuing) =>
            {
                // In This one, the first change gives the geometry its own copy of the entry, which it then goes to.
                if (!continuing && ApplyToOne)
                    SplitRequested?.Invoke();
                DrawableMaterial target = Material ?? material;
                float[] values = ShaderEdits.Current(target, value.Name);
                if (component < values.Length)
                {
                    values[component] = number;
                    edits.Set(target, value.Name, values, continuing);
                }
            };
            field.Ended += edits.Close;
            field.TypingStarted += () => TypingStarted?.Invoke();
            field.TypingEnded += () => TypingEnded?.Invoke();
            fields[i] = field;
            line.Children.Add(field);
        }
        rows.Add(new Row(value, fields, reset));
        var head = new DockPanel { Height = 22 };
        // Its place is kept while it's hidden, so every row's fields line up.
        var slot = new Panel { Width = 18, Children = { reset } };
        DockPanel.SetDock(slot, Dock.Right);
        head.Children.Add(slot);
        if (!below)
        {
            line.Margin = new Thickness(6, 0, 4, 0);
            line.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(line, Dock.Right);
            head.Children.Add(line);
        }
        head.Children.Add(label);
        if (!below)
            return head;
        return new StackPanel { Spacing = 1, Children = { head, line } };
    }

    /// <summary>Shows the entry's values as they are now, with a reset beside each that differs from the file's.</summary>
    private void Refresh()
    {
        if (Material is not { } material)
            return;
        foreach (Row row in rows)
        {
            float[] values = ShaderEdits.Current(material, row.Value.Name);
            bool changed = edits.IsChanged(material, row.Value.Name);
            for (int i = 0; i < row.Fields.Length; i++)
            {
                if (!row.Fields[i].Typing && i < values.Length)
                    row.Fields[i].Value = values[i];
                row.Fields[i].Changed = changed;
            }
            row.Reset.IsVisible = changed;
        }
        resetAll.IsVisible = rows.Any(row => row.Reset.IsVisible);
    }
}
