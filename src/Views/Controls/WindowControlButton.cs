using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace HikariHopper.Views.Controls;

/// <summary>
/// A 34px divided title-bar cell (port of <c>WindowControlButton.qml</c>). <see cref="Kind"/> is
/// minimize | maximize | restore | close; clicking raises <see cref="Button.Click"/>.
/// </summary>
public class WindowControlButton : Button
{
    public static readonly StyledProperty<string> KindProperty =
        AvaloniaProperty.Register<WindowControlButton, string>(nameof(Kind), "");

    public static readonly StyledProperty<bool> SeparatedProperty =
        AvaloniaProperty.Register<WindowControlButton, bool>(nameof(Separated), true);

    private WindowControlGlyph? glyph;
    private bool nonClientHover;
    private bool nonClientPressed;

    public string Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public bool Separated
    {
        get => GetValue(SeparatedProperty);
        set => SetValue(SeparatedProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        base.MeasureOverride(availableSize);
        return new Size(34, Ui.Theme.WindowTitleHeight);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        glyph = e.NameScope.Find<WindowControlGlyph>("PART_Glyph");
        Refresh();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsPointerOverProperty || change.Property == IsPressedProperty
            || change.Property == IsEffectivelyEnabledProperty)
            Refresh();
    }

    /// <summary>
    /// The pointer is over the button while Windows treats its area as the window's maximize button, for snap layouts
    /// (<see cref="SnapLayoutHook"/>): it then reaches the button as non-client messages, not as pointer events.
    /// </summary>
    internal bool NonClientHover
    {
        get => nonClientHover;
        set
        {
            nonClientHover = value;
            Refresh();
        }
    }

    /// <summary>The button is held down there, as <see cref="NonClientHover"/>.</summary>
    internal bool NonClientPressed
    {
        get => nonClientPressed;
        set
        {
            nonClientPressed = value;
            Refresh();
        }
    }

    /// <summary>Whether focus came from the keyboard (<c>:focus-visible</c>), the only time it shows.</summary>
    public bool KeyboardFocused => IsFocused && Classes.Contains(":focus-visible");

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

    /// <summary>Qt's ToolButton activates on Space only; Enter is left to the window.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            return;
        base.OnKeyDown(e);
    }

    private void Refresh()
    {
        if (glyph is null)
            return;
        glyph.Pressed = IsPressed || NonClientPressed;
        glyph.Ink = !IsEffectivelyEnabled
            ? Ui.Theme.TextFaint
            : IsPointerOver || NonClientHover || KeyboardFocused ? Ui.Theme.Text : Ui.Theme.TextDim;
    }
}
