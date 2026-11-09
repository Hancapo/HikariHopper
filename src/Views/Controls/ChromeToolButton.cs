using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace HikariHopper.Views.Controls;

/// <summary>
/// Every button in the chrome (port of <c>ChromeToolButton.qml</c>).
/// </summary>
/// <remarks>
/// Three shapes, chosen with a flag rather than by overriding the background at the call site:
/// a ghost (transparent until hovered), a raised chrome button, and the green primary. Overriding
/// the background outside this file is what killed the hover and pressed states before — the
/// variants exist so nobody has to. Set <see cref="Foreground"/> locally to replace the computed
/// ink, the way QML call sites assigned <c>foreground</c>.
/// </remarks>
public class ChromeToolButton : Button
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<ChromeToolButton, string>(nameof(Text), "");

    public static readonly StyledProperty<bool> BorderedProperty =
        AvaloniaProperty.Register<ChromeToolButton, bool>(nameof(Bordered), true);

    /// <summary>The green action. Dark ink, brighter on hover, pressed a shade down.</summary>
    public static readonly StyledProperty<bool> PrimaryProperty =
        AvaloniaProperty.Register<ChromeToolButton, bool>(nameof(Primary));

    /// <summary>A destructive action uses the error colour as its fill.</summary>
    public static readonly StyledProperty<bool> DestructiveProperty =
        AvaloniaProperty.Register<ChromeToolButton, bool>(nameof(Destructive));

    /// <summary>Sits on raised chrome with a bevel instead of being transparent at rest.</summary>
    public static readonly StyledProperty<bool> RaisedProperty =
        AvaloniaProperty.Register<ChromeToolButton, bool>(nameof(Raised));

    /// <summary>A bare mark takes no hover fill; the glyph brightens on its own instead.</summary>
    public static readonly StyledProperty<bool> HoverFillProperty =
        AvaloniaProperty.Register<ChromeToolButton, bool>(nameof(HoverFill), true);

    /// <summary>A selected segment sits in a sunken well instead of lighting up.</summary>
    public static readonly StyledProperty<bool> SunkenProperty =
        AvaloniaProperty.Register<ChromeToolButton, bool>(nameof(Sunken));

    /// <summary>
    /// The button is a segment inside a sunken group: selected (<see cref="Sunken"/>) it sits on a
    /// raised fill instead of sinking into the well.
    /// </summary>
    public static readonly StyledProperty<bool> InWellProperty =
        AvaloniaProperty.Register<ChromeToolButton, bool>(nameof(InWell));

    /// <summary>
    /// The button is a segment of a joined group floating over the viewport (its overlay toggles): a solid raised fill
    /// at rest and no border of its own; on (<see cref="Sunken"/>) it fills with the accent, and disabled its glyph
    /// fades right out, so it doesn't read as merely off.
    /// </summary>
    public static readonly StyledProperty<bool> SegmentProperty =
        AvaloniaProperty.Register<ChromeToolButton, bool>(nameof(Segment));

    /// <summary>The icon's stroke in pixels (1.8 by default, lighter on the toolbar and tabs).</summary>
    public static readonly StyledProperty<double> IconThicknessProperty =
        AvaloniaProperty.Register<ChromeToolButton, double>(nameof(IconThickness), 1.8);

    /// <summary>The icon's square size (16 by default).</summary>
    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<ChromeToolButton, double>(nameof(IconSize), 16);

    /// <summary>Draws a <see cref="ChromeIcon"/> of this kind instead of <see cref="Text"/>.</summary>
    public static readonly StyledProperty<string> IconKindProperty =
        AvaloniaProperty.Register<ChromeToolButton, string>(nameof(IconKind), "");

    private static readonly IImmutableSolidColorBrush PrimaryBevel = new ImmutableSolidColorBrush(Ui.Theme.RgbaColor(1, 1, 1, 0.4));
    private static readonly IImmutableSolidColorBrush ErrorHover = new ImmutableSolidColorBrush(Ui.Theme.Lighter(Ui.Theme.Error.Color, 1.11));
    private static readonly IImmutableSolidColorBrush ErrorDown = new ImmutableSolidColorBrush(Ui.Theme.Darker(Ui.Theme.Error.Color, 1.12));
    private static readonly IImmutableSolidColorBrush SelectionHover = new ImmutableSolidColorBrush(Ui.Theme.Lighter(Ui.Theme.Selection.Color, 1.11));
    private static readonly IImmutableSolidColorBrush SelectionDown = new ImmutableSolidColorBrush(Ui.Theme.Darker(Ui.Theme.Selection.Color, 1.12));
    private static readonly IImmutableSolidColorBrush AccentHover = new ImmutableSolidColorBrush(Ui.Theme.Lighter(Ui.Theme.Accent.Color, 1.11));
    private static readonly IImmutableSolidColorBrush AccentDown = new ImmutableSolidColorBrush(Ui.Theme.Darker(Ui.Theme.Accent.Color, 1.12));
    private static readonly IImmutableSolidColorBrush SegmentDown = new ImmutableSolidColorBrush(Ui.Theme.Darker(Ui.Theme.ChromeRaised.Color, 1.12));

    /// <summary>A disabled segment's glyph, faded well below an off one's.</summary>
    internal const double DisabledSegmentOpacity = .32;

    private Border? background;
    private Border? bevel;
    private ChromeIcon? icon;
    private TextBlock? label;

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool Bordered
    {
        get => GetValue(BorderedProperty);
        set => SetValue(BorderedProperty, value);
    }

    public bool Primary
    {
        get => GetValue(PrimaryProperty);
        set => SetValue(PrimaryProperty, value);
    }

    public bool Destructive
    {
        get => GetValue(DestructiveProperty);
        set => SetValue(DestructiveProperty, value);
    }

    public bool Raised
    {
        get => GetValue(RaisedProperty);
        set => SetValue(RaisedProperty, value);
    }

    public bool HoverFill
    {
        get => GetValue(HoverFillProperty);
        set => SetValue(HoverFillProperty, value);
    }

    public bool Sunken
    {
        get => GetValue(SunkenProperty);
        set => SetValue(SunkenProperty, value);
    }

    public bool Segment
    {
        get => GetValue(SegmentProperty);
        set => SetValue(SegmentProperty, value);
    }

    public bool InWell
    {
        get => GetValue(InWellProperty);
        set => SetValue(InWellProperty, value);
    }

    public double IconThickness
    {
        get => GetValue(IconThicknessProperty);
        set => SetValue(IconThicknessProperty, value);
    }

    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public string IconKind
    {
        get => GetValue(IconKindProperty);
        set => SetValue(IconKindProperty, value);
    }

    public bool Framed => Bordered || Raised || Primary || Destructive;

    /// <summary>The ink currently used for the text or icon: a local <see cref="Foreground"/>, else the state ink.</summary>
    public IBrush CurrentForeground =>
        IsSet(ForegroundProperty) && Foreground is { } local ? local : DefaultForeground;

    private IBrush DefaultForeground => !IsEffectivelyEnabled
        ? Ui.Theme.TextFaint
        : Primary || Destructive
            ? Ui.Theme.SelectionText
            : IsPressed
                ? Ui.Theme.TextRow
                : IsPointerOver ? Ui.Theme.Text : Ui.Theme.TextDim;

    private IBrush FillBase => Destructive ? Ui.Theme.Error : Primary ? Ui.Theme.Selection : Raised ? Ui.Theme.ChromeRaised : Brushes.Transparent;

    private IBrush FillHover => Destructive ? ErrorHover : Primary ? SelectionHover : Ui.Theme.HoverChrome;

    private IBrush FillDown => Destructive ? ErrorDown : Primary ? SelectionDown : Ui.Theme.BorderSoft;

    /// <summary>The QML implicit size is a fixed 32x28 whatever the label.</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        base.MeasureOverride(availableSize);
        return new Size(32, 28);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        background = e.NameScope.Find<Border>("PART_Background");
        bevel = e.NameScope.Find<Border>("PART_Bevel");
        icon = e.NameScope.Find<ChromeIcon>("PART_Icon");
        label = e.NameScope.Find<TextBlock>("PART_Text");
        Refresh();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsPointerOverProperty || change.Property == IsPressedProperty
            || change.Property == IsEffectivelyEnabledProperty || change.Property == ForegroundProperty
            || change.Property == BorderedProperty || change.Property == PrimaryProperty
            || change.Property == DestructiveProperty || change.Property == RaisedProperty
            || change.Property == HoverFillProperty || change.Property == SunkenProperty
            || change.Property == IconKindProperty || change.Property == InWellProperty
            || change.Property == IconSizeProperty || change.Property == CornerRadiusProperty
            || change.Property == IconThicknessProperty || change.Property == SegmentProperty)
            Refresh();
    }

    /// <summary>Qt's ToolButton activates on Space only; Enter is left to the window.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            return;
        base.OnKeyDown(e);
    }

    private void Refresh()
    {
        if (background is null || bevel is null || icon is null || label is null)
            return;
        bool framed = Framed;
        background.Background = !IsEffectivelyEnabled
            ? (framed ? Ui.Theme.ChromeRaised : Brushes.Transparent)
            : Sunken
                ? (InWell ? Ui.Theme.ChromeRaised : Ui.Theme.InsetBg)
                : !HoverFill
                    ? FillBase
                    : IsPressed
                        ? FillDown
                        : IsPointerOver ? FillHover : FillBase;
        background.BorderThickness = new Thickness(framed ? 1 : 0);
        bevel.IsVisible = framed && !Sunken && !IsPressed;
        bevel.Background = (Primary || Destructive) && IsEffectivelyEnabled ? PrimaryBevel : Ui.Theme.Bevel;
        // The 1px top edge stops where the corners start to curve.
        bevel.Margin = new Thickness(Math.Max(1, CornerRadius.TopLeft), 1, Math.Max(1, CornerRadius.TopRight), 0);
        IBrush ink = CurrentForeground;
        if (Segment)
        {
            bool on = Sunken && IsEffectivelyEnabled;
            background.Background = !IsEffectivelyEnabled ? Ui.Theme.ChromeRaised
                : on ? (IsPressed ? AccentDown : IsPointerOver ? AccentHover : Ui.Theme.Accent)
                : IsPressed ? SegmentDown : IsPointerOver ? Ui.Theme.HoverChrome : Ui.Theme.ChromeRaised;
            background.BorderThickness = new Thickness(0);
            bevel.IsVisible = false;
            if (on && !IsSet(ForegroundProperty))
                ink = Ui.Theme.Text;
        }
        icon.Opacity = label.Opacity = Segment && !IsEffectivelyEnabled ? DisabledSegmentOpacity : 1;
        icon.Width = icon.Height = IconSize;
        icon.Thickness = IconThickness;
        icon.IsVisible = IconKind.Length > 0;
        icon.Stroke = ink;
        label.IsVisible = IconKind.Length == 0;
        label.Foreground = ink;
    }
}
