using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;

namespace HikariHopper.Views.Controls;

/// <summary>
/// Compact content-width top-level menu title (port of <c>MenuBarButton.qml</c>): the label's
/// width plus the horizontal padding (12px each side by default), <see cref="Theme.MenuHeight"/>
/// tall, with a rounded highlight while hovered or focused from the keyboard. Bind <see cref="MenuOpen"/> to the popup's visibility so the
/// title stays lit while its menu is open.
/// </summary>
public class MenuBarButton : Button
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<MenuBarButton, string>(nameof(Text), "");

    public static readonly StyledProperty<bool> MenuOpenProperty =
        AvaloniaProperty.Register<MenuBarButton, bool>(nameof(MenuOpen));

    private Border? background;
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
        label = e.NameScope.Find<TextBlock>("PART_Text");
        Refresh();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsPointerOverProperty || change.Property == IsPressedProperty
            || change.Property == MenuOpenProperty)
            Refresh();
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
        if (background is null || label is null)
            return;
        // While its menu is open the title is the top of the menu: the menu's surface, outlined on
        // three sides and square at the bottom, where the menu carries on.
        background.CornerRadius = MenuOpen ? new CornerRadius(Ui.Theme.ItemRadius, Ui.Theme.ItemRadius, 0, 0) : Ui.Theme.ItemCorner;
        background.BorderBrush = Ui.Theme.Border;
        background.BorderThickness = MenuOpen ? new Thickness(1, 1, 1, 0) : default;
        background.Background = MenuOpen
            ? Ui.Theme.ChromeRaised
            : IsPressed ? Ui.Theme.BorderSoft
            : IsPointerOver || KeyboardFocused ? Ui.Theme.HoverChrome
            : Brushes.Transparent;
        label.Foreground = Ui.Theme.Text;
    }
}
