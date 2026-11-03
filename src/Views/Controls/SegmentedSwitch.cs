using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Metadata;

namespace HikariHopper.Views.Controls;

/// <summary>
/// One side of a <see cref="SegmentedSwitch"/>: a glyph or a word, its width, and its ink.
/// </summary>
public sealed class SwitchSide
{
    /// <summary>A <see cref="ChromeIcon"/> kind; or set <see cref="Text"/>.</summary>
    public string Icon { get; set; } = "";

    public string Text { get; set; } = "";

    /// <summary>Its name for screen readers, and what its tooltip says when <see cref="ToolTip"/> is empty.</summary>
    public string Label { get; set; } = "";

    public string ToolTip { get; set; } = "";

    public double Width { get; set; } = 28;

    public double IconSize { get; set; } = 14;

    /// <summary>Its ink while it is in use, or under the pointer; the bright text colour if unset.</summary>
    public IBrush? Ink { get; set; }

    /// <summary>Its ink otherwise; the dim text colour if unset.</summary>
    public IBrush? DimInk { get; set; }
}

/// <summary>
/// A choice among a few sides, as one switch in the toolbar's well styling: a sunken well holding every
/// side, and the raised segment under the side in use, which slides to another side when it changes. The
/// segment belongs to the switch, not to a button of its own: it is rounded only where it reaches an end of
/// the well, and straight wherever it meets a neighbour.
/// </summary>
/// <remarks>
/// A click on a side asks for it; Left / Right ask for the neighbour, and Space for the next side (with
/// two, the other one). It never moves itself: it raises <see cref="SideRequested"/>, and the owner sets
/// <see cref="SelectedIndex"/>. At <c>-1</c> no side is in use, and the segment is hidden.
/// </remarks>
public sealed class SegmentedSwitch : Control
{
    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<SegmentedSwitch, int>(nameof(SelectedIndex));

    // The segment sits this far inside the well, all round, as a button does in a well.
    private const double Inset = 2;

    /// <summary>How long the segment takes to slide to another side.</summary>
    internal static TimeSpan SlideDuration { get; set; } = TimeSpan.FromMilliseconds(150);

    private readonly Border well = new()
    {
        Background = Ui.Theme.InsetBg,
        CornerRadius = Ui.Theme.ControlCorner,
        // A rim only while focus comes from the keyboard; clear otherwise, so nothing moves when it shows.
        BorderBrush = Brushes.Transparent,
        BorderThickness = new Thickness(1),
        Padding = new Thickness(Inset - 1),
    };

    private readonly Border segment = new()
    {
        Background = Ui.Theme.ChromeRaised,
        HorizontalAlignment = HorizontalAlignment.Left,
        Transitions =
        [
            new TransformOperationsTransition
            {
                Property = RenderTransformProperty,
                Duration = SlideDuration,
                Easing = new CubicEaseOut(),
            },
            new DoubleTransition
            {
                Property = WidthProperty,
                Duration = SlideDuration,
                Easing = new CubicEaseOut(),
            },
        ],
    };

    private readonly StackPanel row = new() { Orientation = Orientation.Horizontal };
    private readonly List<SideView> views = [];
    private int hovered = -1;

    public SegmentedSwitch()
    {
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        Height = 28;
        VerticalAlignment = VerticalAlignment.Center;
        // It is as wide as its well; a stretched one would take clicks far from it.
        HorizontalAlignment = HorizontalAlignment.Left;
        well.Child = new Panel { Children = { segment, row } };
        VisualChildren.Add(well);
        LogicalChildren.Add(well);
        Sides.CollectionChanged += (_, _) => Rebuild();
    }

    /// <summary>The user asked for the side at this index.</summary>
    public event EventHandler<int>? SideRequested;

    /// <summary>The sides, first to last.</summary>
    [Content]
    public AvaloniaList<SwitchSide> Sides { get; } = [];

    /// <summary>The side in use, or <c>-1</c> for none.</summary>
    public int SelectedIndex
    {
        get => GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    /// <summary>The raised segment, which slides between the sides.</summary>
    internal Border Segment => segment;

    /// <summary>How far the segment sits from the first side.</summary>
    internal double SegmentOffset => segment.RenderTransform is TransformOperations operations ? operations.Value.M31 : 0;

    /// <summary>Whether focus came from the keyboard (<c>:focus-visible</c>), the only time it shows.</summary>
    internal bool KeyboardFocused => IsFocused && Classes.Contains(":focus-visible");

    /// <summary>The ink a side is drawn in now.</summary>
    internal IBrush InkOf(int index) => views[index].CurrentInk;

    /// <summary>Where a side starts, from the switch's left edge.</summary>
    public double SideStart(int index) => Inset + Sides.Take(index).Sum(side => side.Width);

    /// <summary>Asks for the next side, wrapping round, as Space does.</summary>
    public void Advance()
    {
        if (Sides.Count > 0)
            SideRequested?.Invoke(this, SelectedIndex < 0 ? 0 : (SelectedIndex + 1) % Sides.Count);
    }

    /// <summary>Applies changes made to the sides themselves (their ink, words or widths).</summary>
    public void RefreshSides() => Rebuild();

    protected override Size MeasureOverride(Size availableSize)
    {
        well.Measure(availableSize);
        return well.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        well.Arrange(new Rect(finalSize));
        return finalSize;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedIndexProperty || change.Property == IsFocusedProperty || change.Property == IsEffectivelyEnabledProperty)
            Refresh();
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        Refresh();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        Refresh();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        SetHovered(SideAt(e.GetPosition(this).X));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SetHovered(-1);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        if (SideAt(e.GetPosition(this).X) is var side and >= 0)
            Request(side);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.Space:
                Advance();
                break;
            case Key.Left:
                Request(Math.Max(0, SelectedIndex - 1));
                break;
            case Key.Right:
                Request(Math.Min(Sides.Count - 1, SelectedIndex + 1));
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    /// <summary>Asks for <paramref name="index"/>, unless it is already in use.</summary>
    private void Request(int index)
    {
        if (index >= 0 && index < Sides.Count && index != SelectedIndex)
            SideRequested?.Invoke(this, index);
    }

    /// <summary>The side under <paramref name="x"/>, or <c>-1</c> off the sides.</summary>
    private int SideAt(double x)
    {
        double start = Inset;
        for (int index = 0; index < Sides.Count; index++)
        {
            start += Sides[index].Width;
            if (x < start)
                return index;
        }
        return Sides.Count - 1;
    }

    private void SetHovered(int index)
    {
        if (hovered == index)
            return;
        hovered = index;
        RefreshInk();
    }

    private void Rebuild()
    {
        row.Children.Clear();
        views.Clear();
        foreach (SwitchSide side in Sides)
        {
            var view = new SideView(side);
            views.Add(view);
            row.Children.Add(view.Content);
            string tip = side.ToolTip.Length > 0 ? side.ToolTip : side.Label;
            if (tip.Length > 0)
                ThemedToolTip.Set(view.Content, tip);
        }
        Refresh();
    }

    private void Refresh()
    {
        well.BorderBrush = KeyboardFocused ? Ui.Theme.Accent : Brushes.Transparent;
        int index = SelectedIndex;
        bool shown = index >= 0 && index < Sides.Count;
        segment.IsVisible = shown;
        if (shown)
        {
            // Rounded only where it reaches an end of the well; straight where it meets a neighbour.
            double radius = Ui.Theme.InnerRadius;
            double left = index == 0 ? radius : 0;
            double right = index == Sides.Count - 1 ? radius : 0;
            segment.CornerRadius = new CornerRadius(left, right, right, left);
            segment.Width = Sides[index].Width;
            TransformOperations.Builder slide = TransformOperations.CreateBuilder(1);
            slide.AppendTranslate(SideStart(index) - Inset, 0);
            segment.RenderTransform = slide.Build();
        }
        RefreshInk();
        AutomationProperties.SetName(this, shown ? Sides[index].Label : "");
    }

    /// <summary>The side in use is in full ink, and so is the one under the pointer.</summary>
    private void RefreshInk()
    {
        bool enabled = IsEffectivelyEnabled;
        for (int index = 0; index < views.Count; index++)
            views[index].Paint(!enabled ? null : index == SelectedIndex || index == hovered, enabled);
    }

    /// <summary>One side as drawn: a glyph or a word, centred in its width.</summary>
    private sealed class SideView
    {
        private readonly SwitchSide side;
        private readonly ChromeIcon icon;
        private readonly TextBlock text;

        public SideView(SwitchSide side)
        {
            this.side = side;
            icon = new ChromeIcon
            {
                Kind = side.Icon,
                Width = side.IconSize,
                Height = side.IconSize,
                Thickness = 1.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsVisible = side.Text.Length == 0,
            };
            text = new TextBlock
            {
                Text = side.Text,
                FontFamily = Ui.Theme.UiFont,
                FontSize = Ui.Theme.FontSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsVisible = side.Text.Length > 0,
            };
            // A clear background, so the side takes the pointer for its tooltip.
            Content = new Panel { Width = side.Width, Background = Brushes.Transparent, Children = { icon, text } };
        }

        public Panel Content { get; }

        public IBrush CurrentInk { get; private set; } = Ui.Theme.TextDim;

        /// <summary>Bright in use or under the pointer, dim otherwise, faint (<see langword="null"/>) when disabled.</summary>
        public void Paint(bool? bright, bool enabled)
        {
            CurrentInk = !enabled || bright is null ? Ui.Theme.TextFaint
                : bright.Value ? side.Ink ?? Ui.Theme.Text
                : side.DimInk ?? Ui.Theme.TextDim;
            icon.Stroke = CurrentInk;
            text.Foreground = CurrentInk;
        }
    }
}
