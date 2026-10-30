using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace HikariHopper.Views.Controls;

/// <summary>
/// The house check box (port of <c>SquareCheckBox.qml</c>). Bind <see cref="ToggleButton.IsChecked"/>
/// (QML <c>checked</c>); <see cref="ToggleButton.IsCheckedChanged"/> is QML <c>toggled</c>.
/// </summary>
public class SquareCheckBox : CheckBox
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<SquareCheckBox, string>(nameof(Text), "");

    private Border? indicator;
    private Border? check;
    private TextBlock? label;

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        indicator = e.NameScope.Find<Border>("PART_Indicator");
        check = e.NameScope.Find<Border>("PART_Check");
        label = e.NameScope.Find<TextBlock>("PART_Text");
        Refresh();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsPointerOverProperty || change.Property == IsFocusedProperty
            || change.Property == IsCheckedProperty || change.Property == IsEffectivelyEnabledProperty)
            Refresh();
    }

    /// <summary>Qt's CheckBox toggles on Space only; Enter is left to the window.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            return;
        base.OnKeyDown(e);
    }

    private void Refresh()
    {
        if (indicator is null || check is null || label is null)
            return;
        indicator.BorderBrush = IsFocused || IsPointerOver ? Ui.Theme.Accent : Ui.Theme.Border;
        check.IsVisible = IsChecked == true;
        label.Foreground = IsEffectivelyEnabled ? Ui.Theme.TextRow : Ui.Theme.TextFaint;
    }
}
