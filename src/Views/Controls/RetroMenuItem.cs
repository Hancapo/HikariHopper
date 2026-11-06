using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace HikariHopper.Views.Controls;

/// <summary>
/// A command-rail menu row (port of <c>RetroMenuItem.qml</c>): <see cref="Theme.MenuItemHeight"/>
/// tall, a rounded <c>hoverChrome</c> highlight, a stable 16px cell for the check or icon, and
/// right-aligned faint mono <see cref="ShortcutText"/>.
/// </summary>
/// <remarks>
/// <see cref="Text"/> is QML <c>text</c> (or a submenu's <c>title</c>), <see cref="IconName"/> is
/// <c>iconName</c>/<c>icon.name</c>, <see cref="Checkable"/>/<see cref="MenuItem.IsChecked"/> are
/// <c>checkable</c>/<c>checked</c>, <see cref="AutoExclusive"/> makes checkable siblings exclusive.
/// <see cref="Triggered"/> is <c>triggered</c>; <see cref="Toggled"/> is <c>toggled</c>.
/// Add child items to make it a submenu, which opens 4px clear of this menu's frame.
/// </remarks>
public class RetroMenuItem : MenuItem
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<RetroMenuItem, string>(nameof(Text), "");

    public static readonly StyledProperty<string> ShortcutTextProperty =
        AvaloniaProperty.Register<RetroMenuItem, string>(nameof(ShortcutText), "");

    public static readonly StyledProperty<string> IconNameProperty =
        AvaloniaProperty.Register<RetroMenuItem, string>(nameof(IconName), "");

    public static readonly StyledProperty<bool> CheckableProperty =
        AvaloniaProperty.Register<RetroMenuItem, bool>(nameof(Checkable));

    public static readonly StyledProperty<bool> AutoExclusiveProperty =
        AvaloniaProperty.Register<RetroMenuItem, bool>(nameof(AutoExclusive));

    private Border? background;
    private Control? indicator;
    private ChromeIcon? check;
    private LucideIcon? icon;
    private ChromeIcon? arrow;
    private TextBlock? label;
    private TextBlock? shortcut;
    private Control? content;
    private Border? bridge;
    private Panel? submenuFrame;
    // Avalonia toggles IsChecked just before raising Click, in the same call; this marks that window.
    private bool checkChangedPending;

    public event EventHandler<RoutedEventArgs>? Triggered;

    public event EventHandler<RoutedEventArgs>? Toggled;

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string ShortcutText
    {
        get => GetValue(ShortcutTextProperty);
        set => SetValue(ShortcutTextProperty, value);
    }

    public string IconName
    {
        get => GetValue(IconNameProperty);
        set => SetValue(IconNameProperty, value);
    }

    public bool Checkable
    {
        get => GetValue(CheckableProperty);
        set => SetValue(CheckableProperty, value);
    }

    public bool AutoExclusive
    {
        get => GetValue(AutoExclusiveProperty);
        set => SetValue(AutoExclusiveProperty, value);
    }

    /// <summary>Qt <c>highlighted</c>: hovered or the keyboard's current row.</summary>
    public bool Highlighted => IsSelected;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        background = e.NameScope.Find<Border>("PART_Background");
        indicator = e.NameScope.Find<Control>("PART_Indicator");
        check = e.NameScope.Find<ChromeIcon>("PART_Check");
        icon = e.NameScope.Find<LucideIcon>("PART_Icon");
        arrow = e.NameScope.Find<ChromeIcon>("PART_Arrow");
        label = e.NameScope.Find<TextBlock>("PART_Text");
        shortcut = e.NameScope.Find<TextBlock>("PART_Shortcut");
        content = e.NameScope.Find<Control>("PART_Content");
        bridge = e.NameScope.Find<Border>("PART_SubmenuBridge");
        submenuFrame = e.NameScope.Find<Panel>("PART_SubmenuFrame");
        if (submenuFrame is not null)
        {
            Thickness shadow = Ui.Theme.MenuShadowMargin;
            submenuFrame.Margin = new Thickness(0, 0, shadow.Left + shadow.Right, shadow.Bottom);
        }
        if (bridge is not null)
            bridge.Padding = new Thickness(SubmenuGap, 0, 0, 0);
        if (e.NameScope.Find<Popup>("PART_Popup") is { } popup)
        {
            popup.Placement = PlacementMode.Custom;
            popup.CustomPopupPlacementCallback = PlaceSubmenu;
        }
        Refresh();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
            Header = Text;
        if (change.Property == IsCheckedProperty && !checkChangedPending)
        {
            checkChangedPending = true;
            Dispatcher.UIThread.Post(() => checkChangedPending = false);
        }
        if (change.Property == CheckableProperty || change.Property == AutoExclusiveProperty)
            ToggleType = !Checkable ? MenuItemToggleType.None : AutoExclusive ? MenuItemToggleType.Radio : MenuItemToggleType.CheckBox;
        if (change.Property == IsSelectedProperty || change.Property == IsCheckedProperty
            || change.Property == IsEffectivelyEnabledProperty || change.Property == CheckableProperty
            || change.Property == IconNameProperty || change.Property == ShortcutTextProperty
            || change.Property == ItemCountProperty)
            Refresh();
    }

    protected override void OnClick(RoutedEventArgs e)
    {
        base.OnClick(e);
        bool toggled = Checkable && checkChangedPending;
        checkChangedPending = false;
        Triggered?.Invoke(this, e);
        if (toggled)
            Toggled?.Invoke(this, e);
    }

    /// <summary>The gap between a menu's frame and the submenu it opens.</summary>
    public const double SubmenuGap = 4;

    /// <summary>
    /// QQuickMenu cascading, with a gap: the submenu opens 4px clear of this menu's frame with its
    /// first row level with this one; when it does not fit it opens on the left instead
    /// (Avalonia's flip would keep the right-hand offset). The gap is the popup's own transparent
    /// padding on the side facing this menu: Avalonia closes a submenu once the pointer has been
    /// off it for 400ms, so the pointer must never leave it on the way across.
    /// </summary>
    private void PlaceSubmenu(CustomPopupPlacement placement)
    {
        placement.ConstraintAdjustment = PopupPositionerConstraintAdjustment.SlideX | PopupPositionerConstraintAdjustment.SlideY;
        // This row sits MenuPadding inside its menu's frame, so the frame edge is that far out.
        double reach = Ui.Theme.MenuPadding;
        double right = Bounds.Width;
        double available = TopLevel.GetTopLevel(this) is { } top && this.TranslatePoint(new Point(right, 0), top) is { } edge
            ? top.Bounds.Width - edge.X
            : double.PositiveInfinity;
        bool opensRight = placement.PopupSize.Width + reach <= available;
        // Moving the padding to the other side keeps the popup's size, so this does not re-measure it.
        if (bridge is not null)
            bridge.Padding = opensRight ? new Thickness(SubmenuGap, 0, 0, 0) : new Thickness(0, 0, SubmenuGap, 0);
        // The shadow's room goes on the far side and below; on the near side it would fall on this
        // menu and the transparent margin would take the pointer from its rows.
        Thickness shadow = Ui.Theme.MenuShadowMargin;
        if (submenuFrame is not null)
            submenuFrame.Margin = opensRight ? new Thickness(0, 0, shadow.Left + shadow.Right, shadow.Bottom) : new Thickness(shadow.Left + shadow.Right, 0, 0, shadow.Bottom);
        if (opensRight)
        {
            placement.Anchor = PopupAnchor.TopRight;
            placement.Gravity = PopupGravity.BottomRight;
            placement.Offset = new Point(reach, -Ui.Theme.MenuPadding);
        }
        else
        {
            placement.Anchor = PopupAnchor.TopLeft;
            placement.Gravity = PopupGravity.BottomLeft;
            placement.Offset = new Point(-reach, -Ui.Theme.MenuPadding);
        }
    }

    private void Refresh()
    {
        if (background is null || indicator is null || check is null || icon is null
            || arrow is null || label is null || shortcut is null || content is null)
            return;
        bool highlighted = Highlighted;
        bool enabled = IsEffectivelyEnabled;
        IBrush mark = highlighted ? Ui.Theme.Text : Ui.Theme.TextDim;
        background.Background = highlighted ? Ui.Theme.HoverChrome : Brushes.Transparent;
        indicator.IsVisible = Checkable || IconName.Length > 0;
        check.IsVisible = IsChecked;
        check.Stroke = mark;
        icon.IsVisible = !Checkable && IconName.Length > 0;
        icon.Stroke = mark;
        arrow.IsVisible = HasSubMenu;
        arrow.Stroke = mark;
        label.Foreground = !enabled ? Ui.Theme.TextFaint : highlighted ? Ui.Theme.Text : Ui.Theme.TextRow;
        shortcut.IsVisible = ShortcutText.Length > 0;
        // Dim, not faint: the menu is a raised surface, where faint ink is too light to read at this size.
        shortcut.Foreground = enabled ? Ui.Theme.TextDim : Ui.Theme.TextFaint;
        content.Margin = new Thickness(32, 0, HasSubMenu ? 26 : 10, 0);
    }
}
