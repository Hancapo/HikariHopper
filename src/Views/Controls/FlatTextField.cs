using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;

namespace HikariHopper.Views.Controls;

/// <summary>
/// Every text input (port of <c>FlatTextField.qml</c>): a rounded sunken well that lightens on
/// hover and takes an accent border while focused, an error border when <see cref="Invalid"/>.
/// </summary>
/// <remarks>
/// <see cref="TextBox.PlaceholderText"/> is QML <c>placeholderText</c>. <see cref="EditingFinished"/>
/// fires when Return/Enter is pressed or the field loses focus, like Qt's <c>editingFinished</c>.
/// </remarks>
public class FlatTextField : TextBox
{
    /// <summary>A key cap rendered inside the field, the way a desktop tool advertises its shortcut.</summary>
    public static readonly StyledProperty<string> KeyCapProperty =
        AvaloniaProperty.Register<FlatTextField, string>(nameof(KeyCap), "");

    /// <summary>A <see cref="ChromeIcon"/> kind drawn at the left edge.</summary>
    public static readonly StyledProperty<string> LeadingIconProperty =
        AvaloniaProperty.Register<FlatTextField, string>(nameof(LeadingIcon), "");

    public static readonly StyledProperty<IBrush> LeadingColorProperty =
        AvaloniaProperty.Register<FlatTextField, IBrush>(nameof(LeadingColor), Ui.Theme.TextFaint);

    /// <summary>Room kept free before the key cap, for controls laid over the field's right end.</summary>
    public static readonly StyledProperty<double> TrailingSpaceProperty =
        AvaloniaProperty.Register<FlatTextField, double>(nameof(TrailingSpace));

    public static readonly StyledProperty<bool> InvalidProperty =
        AvaloniaProperty.Register<FlatTextField, bool>(nameof(Invalid));

    /// <summary>Qt's <c>editingFinished</c>: Return/Enter, or focus leaving the field.</summary>
    public static readonly RoutedEvent<RoutedEventArgs> EditingFinishedEvent =
        RoutedEvent.Register<FlatTextField, RoutedEventArgs>(nameof(EditingFinished), RoutingStrategies.Bubble);

    private static readonly IImmutableSolidColorBrush HoverWell = new ImmutableSolidColorBrush(Ui.Theme.Lighter(Ui.Theme.InsetBg.Color, 1.35));

    private Border? background;
    private Control? leadingIcon;
    private Control? keyCapPlate;
    private Control? placeholder;
    private TextPresenter? presenter;

    static FlatTextField()
    {
        CaretBlinkIntervalProperty.OverrideDefaultValue<FlatTextField>(SystemCaretBlinkInterval());
    }

    public FlatTextField()
    {
        UpdatePadding();
    }

    public event EventHandler<RoutedEventArgs>? EditingFinished
    {
        add => AddHandler(EditingFinishedEvent, value);
        remove => RemoveHandler(EditingFinishedEvent, value);
    }

    public double TrailingSpace
    {
        get => GetValue(TrailingSpaceProperty);
        set => SetValue(TrailingSpaceProperty, value);
    }

    public string KeyCap
    {
        get => GetValue(KeyCapProperty);
        set => SetValue(KeyCapProperty, value);
    }

    public string LeadingIcon
    {
        get => GetValue(LeadingIconProperty);
        set => SetValue(LeadingIconProperty, value);
    }

    public IBrush LeadingColor
    {
        get => GetValue(LeadingColorProperty);
        set => SetValue(LeadingColorProperty, value);
    }

    public bool Invalid
    {
        get => GetValue(InvalidProperty);
        set => SetValue(InvalidProperty, value);
    }

    /// <summary>QML <c>implicitHeight</c>: the background has none, so the field is one Qt text line tall
    /// (plus the zero vertical padding) unless a height is set.</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        Size size = base.MeasureOverride(availableSize);
        double line = QtTextMetrics.LineHeight(FontFamily, FontSize, FontWeight, FontStyle);
        return double.IsNaN(line) ? size : new Size(size.Width, Math.Min(availableSize.Height, line + Padding.Top + Padding.Bottom));
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        background = e.NameScope.Find<Border>("PART_Background");
        leadingIcon = e.NameScope.Find<Control>("PART_LeadingIcon");
        keyCapPlate = e.NameScope.Find<Control>("PART_KeyCap");
        placeholder = e.NameScope.Find<Control>("PART_Placeholder");
        presenter = e.NameScope.Find<TextPresenter>("PART_TextPresenter");
        UpdatePadding();
        Refresh();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LeadingIconProperty || change.Property == KeyCapProperty || change.Property == TrailingSpaceProperty)
            UpdatePadding();
        // Qt's TextInput moves its cursor to the end whenever the text is replaced from outside.
        if (change.Property == TextProperty && !IsKeyboardFocusWithin)
        {
            // Moving the caret before the template's presenter has the new text leaves the presenter's
            // caret and selection out of sync for good (a later SelectAll never shows); wait for it.
            if (presenter is null || presenter.Text == Text)
                CaretIndex = Text?.Length ?? 0;
            else
                Dispatcher.UIThread.Post(() =>
                {
                    if (!IsKeyboardFocusWithin)
                        CaretIndex = Text?.Length ?? 0;
                });
        }
        if (change.Property == IsPointerOverProperty || change.Property == IsKeyboardFocusWithinProperty
            || change.Property == InvalidProperty || change.Property == LeadingIconProperty
            || change.Property == KeyCapProperty || change.Property == TextProperty
            || change.Property == CornerRadiusProperty || change.Property == BoundsProperty)
            Refresh();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            RaiseEvent(new RoutedEventArgs(EditingFinishedEvent));
        base.OnKeyDown(e);
    }

    /// <summary>Qt's TextInput keeps its cursor when focused by Tab; it never selects all.</summary>
    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        int caret = CaretIndex;
        base.OnGotFocus(e);
        if (e.NavigationMethod == NavigationMethod.Tab)
        {
            SelectionStart = caret;
            SelectionEnd = caret;
            CaretIndex = caret;
        }
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        RaiseEvent(new RoutedEventArgs(EditingFinishedEvent));
    }

    /// <summary>leftPadding 10 (32 with a leading icon), rightPadding 10 (40 with a key cap), no vertical padding.</summary>
    private void UpdatePadding() =>
        Padding = new Thickness(LeadingIcon.Length == 0 ? 10 : 32, 0, (KeyCap.Length == 0 ? 10 : 40) + TrailingSpace, 0);

    private void Refresh()
    {
        if (background is null)
            return;
        bool focused = IsKeyboardFocusWithin;
        // A short field (the texture tools' number boxes) keeps a quarter of its height as its
        // radius, so it never turns into a pill.
        double radius = Math.Min(CornerRadius.TopLeft, Math.Floor(Bounds.Height / 4));
        background.CornerRadius = new CornerRadius(Math.Max(0, radius));
        background.Background = IsPointerOver && !focused ? HoverWell : Ui.Theme.InsetBg;
        background.BorderBrush = Invalid
            ? Ui.Theme.Error
            : focused ? Ui.Theme.Accent
            : IsPointerOver ? Ui.Theme.Border : Ui.Theme.BorderHard;
        if (leadingIcon is not null)
            leadingIcon.IsVisible = LeadingIcon.Length > 0;
        if (keyCapPlate is not null)
            keyCapPlate.IsVisible = KeyCap.Length > 0;
        if (placeholder is not null)
            placeholder.IsVisible = string.IsNullOrEmpty(Text);
    }

    /// <summary>Qt blinks the cursor at the system caret rate (GetCaretBlinkTime per phase).</summary>
    private static TimeSpan SystemCaretBlinkInterval()
    {
        if (!OperatingSystem.IsWindows())
            return TimeSpan.FromMilliseconds(500);
        uint milliseconds = GetCaretBlinkTime();
        return milliseconds is 0 or uint.MaxValue ? TimeSpan.FromMilliseconds(500) : TimeSpan.FromMilliseconds(milliseconds);
    }

    [DllImport("user32.dll")]
    private static extern uint GetCaretBlinkTime();
}
