using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using FiveFury.Drawables;
using HikariHopper.Rendering;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>
/// A number of a shader value: dragged sideways it changes as it goes (Shift for fine steps), and clicked it opens for
/// typing, Enter or leaving it to keep what was typed, Escape to drop it. On its own it is a slider's well, its fill
/// showing where the number sits in <see cref="Range"/>; in a vector it is a segment of the vector's joined well.
/// </summary>
internal sealed class ScrubField : Border
{
    private static readonly IBrush HoverWell = new ImmutableSolidColorBrush(Ui.Theme.Lighter(Ui.Theme.InsetBg.Color, 1.35));
    private static readonly IBrush Fill = new ImmutableSolidColorBrush(Ui.Theme.Accent.Color, .32);

    private readonly TextBlock text;
    private readonly Grid body;
    private readonly Border fill;
    private TextBox? editor;
    private Point? pressedAt;
    private float pressedValue;
    private bool dragging;
    private float value;

    /// <param name="segment">Whether it is a segment of a vector's well, which draws the well: no well, no fill, numbers shortened.</param>
    public ScrubField(bool segment = false)
    {
        Segment = segment;
        Height = 19;
        CornerRadius = segment ? default : Ui.Theme.InnerCorner;
        ClipToBounds = true;
        Background = segment ? Brushes.Transparent : Ui.Theme.InsetBg;
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
        text = new TextBlock
        {
            FontFamily = Ui.Theme.MonoFont,
            FontSize = Ui.Theme.SmallFontSize,
            Foreground = Ui.Theme.TextRow,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        fill = new Border { Background = Fill, IsHitTestVisible = false };
        body = new Grid { ColumnDefinitions = new ColumnDefinitions("0*,1*") };
        body.Children.Add(fill);
        Grid.SetColumnSpan(text, 2);
        body.Children.Add(text);
        Child = body;
        PointerEntered += (_, _) => Paint();
        PointerExited += (_, _) => Paint();
    }

    /// <summary>Whether it is a segment of a vector's joined well.</summary>
    public bool Segment { get; }

    /// <summary>What the fill spans, for a slider; none for a segment.</summary>
    public (float Min, float Max)? Range { get; set; }

    /// <summary>The number shown.</summary>
    public float Value
    {
        get => value;
        set
        {
            this.value = value;
            text.Text = Segment ? Short(value) : Format(value);
            if (Segment)
                ThemedToolTip.Set(this, Format(value));
            double at = Range is { } range && range.Max > range.Min ? Math.Clamp((value - range.Min) / (range.Max - range.Min), 0, 1) : 0;
            body.ColumnDefinitions[0].Width = new GridLength(at, GridUnitType.Star);
            body.ColumnDefinitions[1].Width = new GridLength(1 - at, GridUnitType.Star);
            fill.IsVisible = at > 0;
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

    /// <summary>A number as a slider shows it: up to four decimals, no trailing zeros.</summary>
    internal static string Format(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>
    /// A number as a vector's segment shows it, to fit: two decimals (three for one under a hundredth, so it doesn't
    /// read as nothing), without the leading zero: .23, -.5, .001.
    /// </summary>
    internal static string Short(float value)
    {
        string shown = value.ToString(value != 0 && Math.Abs(value) < .01f ? "0.###" : "0.##", CultureInfo.InvariantCulture);
        return shown.StartsWith("0.", StringComparison.Ordinal) ? shown[1..] : shown.StartsWith("-0.", StringComparison.Ordinal) ? "-" + shown[2..] : shown;
    }

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
        // Rounded to what a slider shows, so a drag back lands where it started.
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
            Padding = new Thickness(2, 0),
            MinHeight = 0,
            MinWidth = 0,
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
        Child = body;
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

    /// <summary>The open field's text box, for tests.</summary>
    internal TextBox? Editor => editor;

    // The well brightens under the pointer, as the app's fields do, and takes the accent's border while typing.
    private void Paint()
    {
        Background = editor is not null || IsPointerOver ? HoverWell : Segment ? Brushes.Transparent : Ui.Theme.InsetBg;
        BorderBrush = editor is not null ? Ui.Theme.BorderAccent : null;
        BorderThickness = new Thickness(editor is not null ? 1 : 0);
    }
}

/// <summary>
/// The values of the shader entry a picked geometry is drawn with, in a card over the view's top left: in groups that
/// fold (the rarely used ones folded to start with, and as left after that), with readable names. A number is a slider
/// to drag or type, a vector one joined well, a colour has its swatch, and a vehicle's paint slot is named and picked
/// from a list. A change is drawn as it is made; a value that differs from the file's has a reset, and Reset all puts
/// back every one. Textures and the shader itself aren't edited here.
/// </summary>
internal sealed class ShaderValuesCard : Border
{
    // The card's width, the room a value's control has, and the room of a colour's numbers beside its swatch.
    private const double CardWidth = 232, ControlWidth = 118, SwatchWidth = 30;

    // The slots a vehicle paint's colour can be, (2, n, n), as SurfaceMaterial.PaintColour reads them.
    internal static readonly string[] PaintSlots = ["Primary", "Secondary", "Pearlescent", "Wheels", "Unpainted", "Interior", "Dashboard"];

    // Which groups are open, by name, as last left: the next card opens them the same way.
    private static readonly Dictionary<string, bool> Opened = new(StringComparer.Ordinal);

    private readonly ShaderEdits edits;
    private readonly TextBlock title;
    private readonly TextBlock shader;
    private readonly StackPanel groups;
    private readonly ScrollViewer scroller;
    private readonly ChromeToolButton resetAll;
    private readonly List<Row> rows = [];
    private readonly List<(string Name, Control Header, Control Body)> sections = [];

    // A value's row: its fields, one per number, its reset, and the swatch or paint slot it shows instead of numbers.
    private sealed record Row(ShaderValue Value, ScrubField[] Fields, ChromeToolButton Reset, Border? Swatch, Control? Numbers, Border? Slot);

    public ShaderValuesCard(ShaderEdits edits)
    {
        this.edits = edits;
        Width = CardWidth;
        Background = Ui.Theme.ChromeBg;
        BorderBrush = Ui.Theme.BorderHard;
        BorderThickness = new Thickness(1);
        CornerRadius = Ui.Theme.PanelCorner;
        ClipToBounds = true;
        title = new TextBlock { FontSize = Ui.Theme.FontSize, FontWeight = FontWeight.SemiBold, Foreground = Ui.Theme.Text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        shader = new TextBlock
        {
            FontFamily = Ui.Theme.MonoFont,
            FontSize = Ui.Theme.SmallFontSize,
            Foreground = Ui.Theme.TextFaint,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        groups = new StackPanel();
        scroller = new ScrollViewer
        {
            Content = groups,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        resetAll = new ChromeToolButton { Text = "Reset all", Height = 21, Width = 70, CornerRadius = Ui.Theme.InnerCorner };
        resetAll.Click += (_, _) =>
        {
            if (Material is { } material)
                edits.ResetAll(material);
        };
        Avalonia.Automation.AutomationProperties.SetName(resetAll, "Reset all the values");
        ThemedToolTip.Set(resetAll, "Put every value back as the file has it");
        var keys = new TextBlock { Text = "Ctrl+Z · Ctrl+S", FontFamily = Ui.Theme.MonoFont, FontSize = Ui.Theme.SmallFontSize, Foreground = Ui.Theme.TextFaint, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(resetAll, Dock.Right);
        var foot = new Border
        {
            BorderBrush = Ui.Theme.BorderHard,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(10, 5, 10, 6),
            Child = new DockPanel { Children = { resetAll, keys } },
        };
        Header = new StackPanel { Children = { new DockPanel { Children = { title, shader } } } };
        DockPanel.SetDock(title, Dock.Left);
        var head = new Border
        {
            BorderBrush = Ui.Theme.BorderHard,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 8, 10, 7),
            Child = Header,
        };
        DockPanel.SetDock(head, Dock.Top);
        DockPanel.SetDock(foot, Dock.Bottom);
        Child = new DockPanel { Children = { head, foot, scroller } };
        Avalonia.Automation.AutomationProperties.SetName(this, "Shader values");
        edits.Changed += material =>
        {
            if (material == Material)
                Refresh();
        };
    }

    /// <summary>The card's header, under the title: where what applies to the whole entry goes.</summary>
    private StackPanel Header { get; }

    /// <summary>The shader entry shown; none while nothing is picked.</summary>
    public DrawableMaterial? Material { get; private set; }

    /// <summary>The values listed, in order.</summary>
    internal IReadOnlyList<ShaderValue> Values => [.. rows.Select(row => row.Value)];

    /// <summary>The groups, in order.</summary>
    internal IReadOnlyList<string> Groups => [.. sections.Select(section => section.Name)];

    /// <summary>Whether a group is open, its values shown.</summary>
    internal bool IsOpen(string group) => sections.FirstOrDefault(section => section.Name == group).Body?.IsVisible == true;

    /// <summary>Opens or folds a group, as a click on its heading does; the next card keeps it so.</summary>
    internal void SetOpen(string group, bool open)
    {
        Opened[group] = open;
        foreach ((string name, Control header, Control body) in sections)
            if (name == group)
            {
                body.IsVisible = open;
                if (header is Border { Child: DockPanel { Children: [LucideIcon chevron, ..] } })
                    chevron.IconName = open ? "chevron-down" : "chevron-right";
            }
    }

    /// <summary>Whether Reset all can be used: a value differs from the file's.</summary>
    internal bool ResetAllShown => resetAll.IsEnabled;

    /// <summary>The title shown: what the geometry belongs to.</summary>
    internal string Title => title.Text ?? "";

    /// <summary>The field of one number of a value; none when it isn't listed.</summary>
    internal ScrubField? Field(string name, int component = 0) =>
        rows.FirstOrDefault(row => row.Value.Name == name)?.Fields.ElementAtOrDefault(component);

    /// <summary>The paint slot a value shows, by name; none when it isn't one.</summary>
    internal string? SlotShown(string name) =>
        rows.FirstOrDefault(row => row.Value.Name == name)?.Slot is { IsVisible: true, Child: DockPanel { Children: [.., TextBlock shown] } } ? shown.Text : null;

    /// <summary>Whether a value has its reset shown, as one that differs from the file's.</summary>
    internal bool ResetShown(string name) => rows.FirstOrDefault(row => row.Value.Name == name)?.Reset.IsVisible == true;

    /// <summary>Resets one value, as its reset does.</summary>
    internal void ResetValue(string name)
    {
        if (Material is { } material && rows.Any(row => row.Value.Name == name))
            edits.Reset(material, name);
    }

    /// <summary>A field opened for typing, and needs the keyboard in the card's window.</summary>
    public event Action? TypingStarted;

    /// <summary>Typing ended: the keyboard goes back to the viewer.</summary>
    public event Action? TypingEnded;

    /// <summary>The tallest the card grows; its values scroll past that.</summary>
    public double Room
    {
        set => scroller.MaxHeight = Math.Max(80, value - 110);
    }

    /// <summary>Shows a shader entry's values, under <paramref name="name"/> (what the geometry belongs to), or nothing.</summary>
    public void Show(string name, DrawableMaterial? material)
    {
        // A field being typed in keeps what was typed, for the entry it was.
        foreach (Row row in rows)
            foreach (ScrubField field in row.Fields)
                field.FinishTyping(true);
        Material = material;
        rows.Clear();
        sections.Clear();
        groups.Children.Clear();
        if (material is null)
            return;
        title.Text = name;
        ShaderDefinition? definition = (material.Gen9Parameters is not null ? ShaderCatalog.Enhanced : ShaderCatalog.Legacy).Find(material.ShaderHash);
        shader.Text = definition?.Name ?? $"0x{material.ShaderHash.Value:X8}";
        foreach (IGrouping<string, ShaderValue> group in ShaderEdits.ValuesOf(material).GroupBy(value => value.Group))
        {
            var body = new StackPanel { Margin = new Thickness(10, 0, 10, 5) };
            foreach (ShaderValue value in group)
                body.Children.Add(Build(material, value));
            Control header = Heading(group.Key, group.Count(), sections.Count > 0);
            sections.Add((group.Key, header, body));
            groups.Children.Add(header);
            groups.Children.Add(body);
            SetOpen(group.Key, Opened.TryGetValue(group.Key, out bool open) ? open : !ShaderEdits.StartsFolded(group.Key));
        }
        if (rows.Count == 0)
            groups.Children.Add(new TextBlock
            {
                Text = "This shader has no values to edit.",
                FontSize = Ui.Theme.SmallFontSize,
                Foreground = Ui.Theme.TextDim,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(10, 8),
            });
        Refresh();
    }

    // A group's heading: a chevron, its name and how many values it holds; a click opens or folds it.
    private Control Heading(string group, int count, bool rule)
    {
        var chevron = new LucideIcon { IconName = "chevron-down", Width = 9, Height = 9, Weight = 1.8, Stroke = Ui.Theme.TextFaint, VerticalAlignment = VerticalAlignment.Center };
        var number = new TextBlock { Text = count.ToString(CultureInfo.InvariantCulture), FontFamily = Ui.Theme.MonoFont, FontSize = Ui.Theme.SmallFontSize, Foreground = Ui.Theme.TextFaint, VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = group, FontSize = Ui.Theme.SmallFontSize, FontWeight = FontWeight.Medium, Foreground = Ui.Theme.TextFaint, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(chevron, Dock.Left);
        DockPanel.SetDock(number, Dock.Right);
        var heading = new Border
        {
            Height = 24,
            Padding = new Thickness(10, 0),
            Background = Brushes.Transparent,
            BorderBrush = Ui.Theme.BorderHard,
            BorderThickness = new Thickness(0, rule ? 1 : 0, 0, 0),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new DockPanel { Children = { chevron, number, name } },
        };
        heading.PointerEntered += (_, _) => name.Foreground = Ui.Theme.TextRow;
        heading.PointerExited += (_, _) => name.Foreground = Ui.Theme.TextFaint;
        heading.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left)
                SetOpen(group, !IsOpen(group));
        };
        Avalonia.Automation.AutomationProperties.SetName(heading, group);
        return heading;
    }

    // A value's row: its label, its reset, and its control: a slider, a vector's joined well (four to a line), a colour's
    // swatch and numbers, or a vehicle paint's slot.
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
            Width = 16,
            Height = 16,
            Bordered = false,
            IconKind = "reset",
            IconSize = 10,
            IconThickness = 1.7,
            Foreground = Ui.Theme.Accent,
            CornerRadius = Ui.Theme.InnerCorner,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false,
        };
        reset.Click += (_, _) => edits.Reset(Material ?? material, value.Name);
        ThemedToolTip.Set(reset, "Put it back as the file has it");
        Avalonia.Automation.AutomationProperties.SetName(reset, $"Reset {value.Label}");

        float[] first = ShaderEdits.Current(material, value.Name);
        bool vector = value.Components > 1;
        var fields = new ScrubField[value.Components];
        for (int i = 0; i < fields.Length; i++)
        {
            int component = i;
            var field = new ScrubField(segment: vector) { Range = vector ? null : value.Range ?? RangeOf(first.ElementAtOrDefault(0)) };
            Avalonia.Automation.AutomationProperties.SetName(field, fields.Length == 1 ? value.Label : $"{value.Label} {component + 1}");
            field.Edited += (number, continuing) =>
            {
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
        }

        // The numbers: a slider, or for a vector its joined wells, a line of up to four each.
        bool colour = value.Colour && value.Components >= 3;
        var lines = new StackPanel { Spacing = 3, HorizontalAlignment = HorizontalAlignment.Right };
        if (!vector)
        {
            fields[0].Width = ControlWidth;
            lines.Children.Add(fields[0]);
        }
        else
            for (int start = 0; start < fields.Length; start += 4)
                lines.Children.Add(Joined(fields[start..Math.Min(fields.Length, start + 4)], colour && start == 0 ? ControlWidth - SwatchWidth - 6 : ControlWidth));
        Border? swatch = null;
        Control numbers = lines;
        if (colour)
        {
            swatch = new Border { Width = SwatchWidth, Height = 19, CornerRadius = Ui.Theme.InnerCorner, BorderBrush = Ui.Theme.BorderHard, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Top };
            numbers = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right, Children = { swatch, lines } };
        }
        // A vehicle paint's colour is one of its paint slots, (2, n, n), which the game fills in.
        Border? slot = value.Legacy.Equals("matDiffuseColor", StringComparison.OrdinalIgnoreCase) && value.Components >= 3 ? Slot(material, value) : null;
        var control = new Panel { Width = ControlWidth, VerticalAlignment = VerticalAlignment.Center, Children = { numbers } };
        if (slot is not null)
            control.Children.Add(slot);
        rows.Add(new Row(value, fields, reset, swatch, numbers, slot));

        var line = new DockPanel { MinHeight = 24, Margin = new Thickness(0, vector && fields.Length > 4 ? 2 : 0, 0, vector && fields.Length > 4 ? 2 : 0) };
        DockPanel.SetDock(control, Dock.Right);
        DockPanel.SetDock(reset, Dock.Right);
        reset.Margin = new Thickness(4, 0, 4, 0);
        line.Children.Add(control);
        line.Children.Add(new Panel { Width = 24, Children = { reset } });
        DockPanel.SetDock(line.Children[^1], Dock.Right);
        line.Children.Add(label);
        label.VerticalAlignment = fields.Length > 4 ? VerticalAlignment.Top : VerticalAlignment.Center;
        if (fields.Length > 4)
            label.Margin = new Thickness(0, 3, 0, 0);
        return line;
    }

    // A vector's numbers in one well, a hairline between each.
    private static Border Joined(ScrubField[] fields, double width)
    {
        var grid = new Grid();
        for (int i = 0; i < fields.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            Grid.SetColumn(fields[i], i);
            grid.Children.Add(fields[i]);
            if (i > 0)
            {
                var hairline = new Border { Width = 1, Background = Ui.Theme.BorderHard, HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false };
                Grid.SetColumn(hairline, i);
                grid.Children.Add(hairline);
            }
        }
        return new Border { Width = width, Height = 19, Background = Ui.Theme.InsetBg, CornerRadius = Ui.Theme.InnerCorner, ClipToBounds = true, Child = grid };
    }

    /// <summary>
    /// What a slider's fill spans when values.json doesn't say: 0 to 1, or for a number past that, 0 to twice it, so the
    /// file's value sits halfway; the same below zero.
    /// </summary>
    internal static (float Min, float Max) RangeOf(float value)
    {
        float size = Math.Abs(value) <= 1 ? 1 : Nice(Math.Abs(value) * 2);
        return value < 0 ? (-size, size) : (0, size);

        static float Nice(float value)
        {
            float power = MathF.Pow(10, MathF.Floor(MathF.Log10(value)));
            float step = value / power;
            return (step <= 1 ? 1 : step <= 2 ? 2 : step <= 5 ? 5 : 10) * power;
        }
    }

    /// <summary>The paint slot a colour is, 1 to 7, when it is one: (2, n, n).</summary>
    internal static int? SlotOf(ReadOnlySpan<float> values) =>
        values.Length >= 3 && MathF.Abs(values[0] - 2) < .001f && MathF.Round(values[1]) is >= 1 and <= 7 && MathF.Abs(values[1] - MathF.Round(values[1])) < .001f
            ? (int)MathF.Round(values[1]) : null;

    // A vehicle paint's slot, named: a click lists the slots, and "Own colour" makes it a colour of its own.
    private Border Slot(DrawableMaterial material, ShaderValue value)
    {
        var shown = new TextBlock { FontSize = Ui.Theme.FontSize - 1, Foreground = Ui.Theme.TextRow, VerticalAlignment = VerticalAlignment.Center };
        var number = new TextBlock { FontSize = Ui.Theme.SmallFontSize, Foreground = Ui.Theme.TextFaint, VerticalAlignment = VerticalAlignment.Center };
        var caret = new LucideIcon { IconName = "chevron-down", Width = 8, Height = 8, Weight = 1.7, Stroke = Ui.Theme.TextDim, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(caret, Dock.Right);
        DockPanel.SetDock(number, Dock.Right);
        var well = new Border
        {
            Height = 19,
            Padding = new Thickness(7, 0),
            Background = Ui.Theme.InsetBg,
            CornerRadius = Ui.Theme.InnerCorner,
            Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new DockPanel { Children = { caret, number, shown } },
        };
        well.PropertyChanged += (_, e) =>
        {
            if (e.Property == TagProperty && well.Tag is int at)
            {
                shown.Text = PaintSlots[at - 1];
                number.Text = $"slot {at}";
            }
        };
        Avalonia.Automation.AutomationProperties.SetName(well, value.Label);
        ThemedToolTip.Set(well, "The paint slot the game fills in: pick another, or a colour of its own");
        well.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left)
                OpenSlots(well, value);
        };
        return well;
    }

    private DropdownWindow? slots;

    // The list of paint slots under the well, and Own colour at its foot.
    private void OpenSlots(Border well, ShaderValue value)
    {
        slots?.Close();
        var list = new StackPanel { Margin = Ui.Theme.MenuInset, MinWidth = 150 };
        void Item(string text, string? detail, Action pick)
        {
            var name = new TextBlock { Text = text, FontSize = Ui.Theme.FontSize, Foreground = Ui.Theme.TextRow, VerticalAlignment = VerticalAlignment.Center };
            var right = new TextBlock { Text = detail ?? "", FontSize = Ui.Theme.SmallFontSize, Foreground = Ui.Theme.TextFaint, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(right, Dock.Right);
            var row = new Border { Height = Ui.Theme.MenuItemHeight, Padding = new Thickness(9, 0), CornerRadius = Ui.Theme.InnerCorner, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = new DockPanel { Children = { right, name } } };
            row.PointerEntered += (_, _) => row.Background = Ui.Theme.HoverChrome;
            row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
            row.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Left)
                    return;
                slots?.Close();
                pick();
            };
            list.Children.Add(row);
        }
        for (int i = 0; i < PaintSlots.Length; i++)
        {
            int at = i + 1;
            Item(PaintSlots[i], $"slot {at}", () => PickSlot(value, at));
        }
        Item("Own colour", null, () => PickSlot(value, null));
        slots = DropdownWindow.OpenUnder(well, list, 3);
        if (slots is not null)
            slots.Closed += (_, _) => slots = null;
    }

    /// <summary>Makes a vehicle paint's colour a paint slot (1 to 7), or a colour of its own (white) for none, as one change.</summary>
    internal void PickSlot(ShaderValue value, int? slot)
    {
        if (Material is not { } material)
            return;
        float[] values = ShaderEdits.Current(material, value.Name);
        if (values.Length < 3)
            return;
        (values[0], values[1], values[2]) = slot is int at ? (2f, at, at) : (1f, 1f, 1f);
        edits.Set(material, value.Name, values);
        edits.Close();
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
            if (row.Swatch is { } swatch && values.Length >= 3)
                swatch.Background = new ImmutableSolidColorBrush(Color.FromRgb(Channel(values[0]), Channel(values[1]), Channel(values[2])));
            if (row.Slot is { } slot)
            {
                int? at = SlotOf(values);
                slot.IsVisible = at is not null;
                slot.Tag = at;
                if (row.Numbers is { } numbers)
                    numbers.IsVisible = at is null;
            }
            row.Reset.IsVisible = changed;
        }
        resetAll.IsEnabled = rows.Any(row => row.Reset.IsVisible);

        static byte Channel(float value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
    }
}
