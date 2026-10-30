using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Media;

namespace HikariHopper.Views.Controls;

/// <summary>
/// Connected command-rail dropdown (port of <c>RetroMenu.qml</c>), used for the menu bar menus
/// and the context menus. Children are <see cref="RetroMenuItem"/>, <see cref="RetroMenuSeparator"/>
/// and <see cref="RetroMenuSection"/>; a submenu is a <see cref="RetroMenuItem"/> with child items
/// (QML nested <c>RetroMenu { title; icon.name }</c>).
/// </summary>
/// <remarks>
/// <see cref="ConnectionWidth"/> is QML <c>connectionWidth</c> (the menu-bar title width); like
/// the QML it has no visible effect, see <see cref="RetroMenuFrame"/>. <see cref="Open(Control, double, double)"/> is QML's
/// <c>menu.x = …; menu.y = …; menu.open()</c>; <see cref="Popup"/> is <c>menu.popup()</c> at the pointer.
/// </remarks>
public class RetroMenu : ContextMenu
{
    public static readonly StyledProperty<double> ConnectionWidthProperty =
        AvaloniaProperty.Register<RetroMenu, double>(nameof(ConnectionWidth));

    public double ConnectionWidth
    {
        get => GetValue(ConnectionWidthProperty);
        set => SetValue(ConnectionWidthProperty, value);
    }

    /// <summary>QML <c>visible</c>.</summary>
    public bool Visible => IsOpen;

    /// <summary>Opens with the menu's top-left corner at (<paramref name="x"/>, <paramref name="y"/>) in <paramref name="target"/>'s coordinates.</summary>
    public void Open(Control target, double x, double y)
    {
        Placement = PlacementMode.AnchorAndGravity;
        PlacementAnchor = PopupAnchor.TopLeft;
        PlacementGravity = PopupGravity.BottomRight;
        PlacementRect = new Rect(x, y, 1, 1);
        HorizontalOffset = 0;
        VerticalOffset = 0;
        Open(target);
    }

    /// <summary>Opens at the pointer, like QML <c>Menu.popup()</c>.</summary>
    public void Popup(Control target)
    {
        Placement = PlacementMode.Pointer;
        PlacementRect = null;
        HorizontalOffset = 0;
        VerticalOffset = 0;
        Open(target);
    }
}

/// <summary>
/// The menu surface as Qt renders RetroMenu.qml's background: chromeRaised with hard left, right
/// and bottom seams and a bevel along the inner left edge (the declared top seam is zero-width).
/// </summary>
public class RetroMenuFrame : Control
{
    public static readonly StyledProperty<double> ConnectionWidthProperty =
        AvaloniaProperty.Register<RetroMenuFrame, double>(nameof(ConnectionWidth));

    static RetroMenuFrame()
    {
        AffectsRender<RetroMenuFrame>(ConnectionWidthProperty);
    }

    public double ConnectionWidth
    {
        get => GetValue(ConnectionWidthProperty);
        set => SetValue(ConnectionWidthProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width;
        double h = Bounds.Height;
        double connection = ConnectionWidth;
        context.FillRectangle(Ui.Theme.ChromeRaised, new Rect(0, 0, w, h));
        context.FillRectangle(Ui.Theme.BorderHard, new Rect(0, 0, 1, h));
        context.FillRectangle(Ui.Theme.BorderHard, new Rect(w - 1, 0, 1, h));
        context.FillRectangle(Ui.Theme.BorderHard, new Rect(0, h - 1, w, 1));
        context.FillRectangle(Ui.Theme.Bevel, new Rect(1, 1, 1, Math.Max(0, h - 2)));
        // RetroMenu.qml declares a top seam and bevel from connectionWidth to the right edge, but
        // gives them `x` plus `anchors.right` and no width, so Qt sizes both to zero and the menu
        // has no top edge at all. Drawn the way Qt renders it; ConnectionWidth is kept for the API.
        _ = connection;
    }
}
