using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.LogicalTree;
using Avalonia.Media;

namespace HikariHopper.Views.Controls;

/// <summary>
/// Connected command-rail dropdown (port of <c>RetroMenu.qml</c>), used for the menu bar menus
/// and the context menus. Children are <see cref="RetroMenuItem"/>, <see cref="RetroMenuSeparator"/>
/// and <see cref="RetroMenuSection"/>; a submenu is a <see cref="RetroMenuItem"/> with child items
/// (QML nested <c>RetroMenu { title; icon.name }</c>).
/// </summary>
/// <remarks>
/// <see cref="ConnectionWidth"/> is the width of the menu-bar title the menu hangs from: the frame leaves
/// its top edge open that far so the lit title and the menu read as one surface (see <see cref="RetroMenuFrame"/>).
/// <see cref="Open(Control, double, double)"/> is QML's <c>menu.x = …; menu.y = …; menu.open()</c>;
/// <see cref="Popup"/> is <c>menu.popup()</c> at the pointer. The menu's margin
/// (<see cref="Ui.Theme.MenuShadowMargin"/>) is room for its shadow: Avalonia places a popup's content, not
/// its margin, at the point given, so the frame still lands there.
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

    private static readonly List<RetroMenu> openMenus = [];

    public RetroMenu()
    {
        Opened += (_, _) => openMenus.Add(this);
        Closed += (_, _) => openMenus.Remove(this);
    }

    /// <summary>
    /// Closes the menu opened last over <c>window</c>, if one is open, and says whether it did: Esc's first job. A
    /// menu opened with the pointer leaves the keyboard with the window, so it doesn't see the key itself.
    /// </summary>
    internal static bool CloseLastOpen(TopLevel window)
    {
        // A menu whose window went away never says it closed.
        openMenus.RemoveAll(menu => !menu.IsOpen || menu.Window is not { IsVisible: true });
        RetroMenu? last = openMenus.LastOrDefault(menu => menu.Window == window);
        if (last is null)
            return false;
        last.Close();
        return true;
    }

    /// <summary>The window the menu was opened over: the first one among what it hangs from.</summary>
    private TopLevel? Window => this.GetLogicalAncestors().OfType<TopLevel>().FirstOrDefault(top => top is not PopupRoot);

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

    /// <summary>
    /// Opens centred under <paramref name="target"/>, <paramref name="gap"/> below it, as a dropdown hangs
    /// from the control that opened it. Near a screen edge it slides along rather than flipping to one side.
    /// </summary>
    public void OpenUnder(Control target, double gap)
    {
        Placement = PlacementMode.AnchorAndGravity;
        PlacementAnchor = PopupAnchor.Bottom;
        PlacementGravity = PopupGravity.Bottom;
        PlacementConstraintAdjustment = PopupPositionerConstraintAdjustment.SlideX | PopupPositionerConstraintAdjustment.FlipY
            | PopupPositionerConstraintAdjustment.SlideY;
        PlacementRect = new Rect(target.Bounds.Size);
        HorizontalOffset = 0;
        VerticalOffset = gap;
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
/// The menu surface: chromeRaised with a 1px border on <see cref="Ui.Theme.TabRadius"/> corners over a
/// soft shadow. Hanging from a menu-bar title (<see cref="ConnectionWidth"/> &gt; 0), its top-left corner
/// is square and its top edge stays open under the title, which draws the rest of the outline.
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
        var bounds = new Rect(Bounds.Size);
        double r = Ui.Theme.TabRadius;
        double connection = Math.Min(ConnectionWidth, bounds.Width - r);
        bool attached = connection > 0;
        var surface = new RoundedRect(bounds, attached ? 0 : r, r, r, r);
        context.DrawRectangle(Ui.Theme.ChromeRaised, null, surface, Ui.Theme.MenuShadow);
        var pen = new Pen(Ui.Theme.Border, 1);
        if (!attached)
        {
            context.DrawRectangle(null, pen, bounds.Deflate(0.5), r - 0.5, r - 0.5);
            return;
        }
        // From the title's right edge round to its left one, which carries on up the title.
        double w = bounds.Width, h = bounds.Height;
        var outline = new StreamGeometry();
        using (StreamGeometryContext path = outline.Open())
        {
            path.BeginFigure(new Point(connection - 1, 0.5), false);
            path.LineTo(new Point(w - r, 0.5));
            path.ArcTo(new Point(w - 0.5, r), new Size(r - 0.5, r - 0.5), 0, false, SweepDirection.Clockwise);
            path.LineTo(new Point(w - 0.5, h - r));
            path.ArcTo(new Point(w - r, h - 0.5), new Size(r - 0.5, r - 0.5), 0, false, SweepDirection.Clockwise);
            path.LineTo(new Point(r, h - 0.5));
            path.ArcTo(new Point(0.5, h - r), new Size(r - 0.5, r - 0.5), 0, false, SweepDirection.Clockwise);
            path.LineTo(new Point(0.5, 0));
            path.EndFigure(false);
        }
        context.DrawGeometry(null, pen, outline);
    }
}
