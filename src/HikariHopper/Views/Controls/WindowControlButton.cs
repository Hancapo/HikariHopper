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

    private Border? focusRule;
    private WindowControlGlyph? glyph;

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
        focusRule = e.NameScope.Find<Border>("PART_FocusRule");
        glyph = e.NameScope.Find<WindowControlGlyph>("PART_Glyph");
        Refresh();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsPointerOverProperty || change.Property == IsPressedProperty
            || change.Property == IsFocusedProperty || change.Property == IsEffectivelyEnabledProperty)
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
        if (focusRule is null || glyph is null)
            return;
        focusRule.IsVisible = IsFocused;
        glyph.Pressed = IsPressed;
        glyph.Ink = !IsEffectivelyEnabled
            ? Ui.Theme.TextFaint
            : IsPointerOver || IsFocused ? Ui.Theme.Text : Ui.Theme.TextDim;
    }
}
