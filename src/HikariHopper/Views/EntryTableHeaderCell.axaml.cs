using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace HikariHopper.Views;

/// <summary>
/// A sortable column header (port of <c>EntryTableHeaderCell.qml</c>, a Qt <c>AbstractButton</c>):
/// hoverBg while hovered or keyboard-focused, textRow ink when sorted or hovered, the up/down caret
/// when <see cref="SortActive"/>, Space clicks it.
/// </summary>
public partial class EntryTableHeaderCell : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<EntryTableHeaderCell, string>(nameof(Text), "");

    public static readonly StyledProperty<bool> SortActiveProperty =
        AvaloniaProperty.Register<EntryTableHeaderCell, bool>(nameof(SortActive));

    public static readonly StyledProperty<bool> SortAscendingProperty =
        AvaloniaProperty.Register<EntryTableHeaderCell, bool>(nameof(SortAscending));

    public static readonly StyledProperty<bool> AlignRightProperty =
        AvaloniaProperty.Register<EntryTableHeaderCell, bool>(nameof(AlignRight));

    public static readonly StyledProperty<double> LeftPaddingProperty =
        AvaloniaProperty.Register<EntryTableHeaderCell, double>(nameof(LeftPadding));

    public static readonly StyledProperty<double> RightPaddingProperty =
        AvaloniaProperty.Register<EntryTableHeaderCell, double>(nameof(RightPadding));

    private bool pressed;
    private bool keyboardFocus;

    public EntryTableHeaderCell()
    {
        InitializeComponent();
        Refresh();
    }

    /// <summary>QML <c>clicked</c>.</summary>
    public event EventHandler? Clicked;

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool SortActive
    {
        get => GetValue(SortActiveProperty);
        set => SetValue(SortActiveProperty, value);
    }

    public bool SortAscending
    {
        get => GetValue(SortAscendingProperty);
        set => SetValue(SortAscendingProperty, value);
    }

    public bool AlignRight
    {
        get => GetValue(AlignRightProperty);
        set => SetValue(AlignRightProperty, value);
    }

    public double LeftPadding
    {
        get => GetValue(LeftPaddingProperty);
        set => SetValue(LeftPaddingProperty, value);
    }

    public double RightPadding
    {
        get => GetValue(RightPaddingProperty);
        set => SetValue(RightPaddingProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == SortActiveProperty
            || change.Property == SortAscendingProperty || change.Property == AlignRightProperty
            || change.Property == LeftPaddingProperty || change.Property == RightPaddingProperty
            || change.Property == IsPointerOverProperty)
            Refresh();
    }

    private void Refresh()
    {
        if (Label is null)
            return;
        bool hovered = IsPointerOver;
        Fill.Background = hovered || keyboardFocus ? Ui.Theme.HoverBg : null;
        Label.Text = Text;
        Label.Foreground = SortActive || hovered ? Ui.Theme.TextRow : Ui.Theme.TextDim;
        Caret.IsVisible = SortActive;
        Caret.Kind = SortAscending ? "caretUp" : "caretDown";
        ContentRow.Margin = new Thickness(LeftPadding, 0, RightPadding, 0);
        ContentRow.HorizontalAlignment = AlignRight ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        AutomationProperties.SetName(this, $"Sort by {Text}");
        AutomationProperties.SetHelpText(this, SortActive ? (SortAscending ? "Sorted ascending" : "Sorted descending") : "Not sorted");
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        // Qt visualFocus: focus that came from the keyboard.
        keyboardFocus = e.NavigationMethod == NavigationMethod.Tab;
        Refresh();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        keyboardFocus = false;
        Refresh();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        pressed = true;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!pressed || e.InitialPressMouseButton != MouseButton.Left)
            return;
        pressed = false;
        e.Pointer.Capture(null);
        e.Handled = true;
        if (new Rect(Bounds.Size).Contains(e.GetPosition(this)))
            Clicked?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        pressed = false;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.None)
        {
            Clicked?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }
}
