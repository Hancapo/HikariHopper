using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;

namespace HikariHopper.Views.Controls;

/// <summary>
/// Compact content-width top-level menu title (port of <c>MenuBarButton.qml</c>): the label's
/// width plus the horizontal padding (12px each side by default), <see cref="Theme.MenuHeight"/>
/// tall, with a rounded highlight. Bind <see cref="MenuOpen"/> to the popup's visibility so the
/// title stays lit while its menu is open.
/// </summary>
public class MenuBarButton : Button
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<MenuBarButton, string>(nameof(Text), "");

    public static readonly StyledProperty<bool> MenuOpenProperty =
        AvaloniaProperty.Register<MenuBarButton, bool>(nameof(MenuOpen));

    private Border? background;
    private Border? focusRule;
    private TextBlock? label;

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool MenuOpen
    {
        get => GetValue(MenuOpenProperty);
        set => SetValue(MenuOpenProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        base.MeasureOverride(availableSize);
        double labelWidth = label is null ? 0 : Math.Ceiling(label.DesiredSize.Width - label.Margin.Left - label.Margin.Right);
        return new Size(labelWidth + Padding.Left + Padding.Right, Ui.Theme.MenuHeight);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        background = e.NameScope.Find<Border>("PART_Background");
        focusRule = e.NameScope.Find<Border>("PART_FocusRule");
        label = e.NameScope.Find<TextBlock>("PART_Text");
        Refresh();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsPointerOverProperty || change.Property == IsPressedProperty
            || change.Property == IsFocusedProperty || change.Property == MenuOpenProperty)
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
        if (background is null || focusRule is null || label is null)
            return;
        background.Background = MenuOpen
            ? Ui.Theme.HoverChrome
            : IsPressed ? Ui.Theme.BorderSoft
            : IsPointerOver ? Ui.Theme.HoverChrome
            : Brushes.Transparent;
        focusRule.IsVisible = IsFocused && !MenuOpen;
        label.Foreground = Ui.Theme.Text;
    }
}
