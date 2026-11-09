using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace HikariHopper.Views;

/// <summary>
/// Controls drawn over the 3D viewport, as Blender draws its toolbar over its view: at a place on its left edge, at a
/// height or centred on it. The viewport is a native window that covers anything drawn in the main window, so they are
/// a window of their own: borderless, transparent, owned by the main window and kept over the viewport as it moves,
/// resizes and hides. It never takes activation, so the keyboard stays with the viewer, and the pointer goes through
/// its empty parts to the viewport.
/// </summary>
internal sealed class ViewportOverlay
{
    private const int WmNcHitTest = 0x0084;
    private const nint HtTransparent = -1;
    private const uint WsExNoActivate = 0x08000000;
    private const uint WsExToolWindow = 0x00000080;

    private readonly Control viewport;
    private readonly Control content;
    // Where it goes: this far in from the viewport's left edge, and this far down from its top, or centred on its height.
    private readonly double left;
    private readonly double? top;
    private Window? window;
    private Window? owner;
    private bool wanted;

    public ViewportOverlay(Control viewport, Control content, double left = 0, double? top = null)
    {
        this.viewport = viewport;
        this.content = content;
        this.left = left;
        this.top = top;
        viewport.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.BoundsProperty || e.Property == Visual.IsVisibleProperty)
                Place();
        };
        viewport.AttachedToVisualTree += (_, _) => Place();
        viewport.DetachedFromVisualTree += (_, _) => Place();
        // Its own size, as what's in it shows and hides, keeps it centred.
        content.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.BoundsProperty)
                Place();
        };
    }

    /// <summary>Its window, once it has been shown.</summary>
    public Window? Window => window;

    /// <summary>Whether it's shown now: wanted, over a viewport that is.</summary>
    public bool IsShown => window?.IsVisible == true;

    /// <summary>Whether it should show, while the viewport does.</summary>
    public bool IsWanted
    {
        get => wanted;
        set
        {
            wanted = value;
            Place();
        }
    }

    /// <summary>Shows it over the viewport's left edge, where it goes, or hides it with the viewport.</summary>
    public void Place()
    {
        Window? main = viewport.IsVisible && viewport.IsEffectivelyVisible ? TopLevel.GetTopLevel(viewport) as Window : null;
        if (!wanted || main is not { IsVisible: true } || viewport.Bounds.Height <= 0)
        {
            Hide();
            return;
        }
        if (owner is not null && owner != main)
            Hide();
        owner = main;
        window ??= Create();
        content.Measure(Size.Infinity);
        double height = Math.Max(content.DesiredSize.Height, content.Bounds.Height);
        window.Position = viewport.PointToScreen(new Point(left, top ?? Math.Round((viewport.Bounds.Height - height) / 2)));
        if (!window.IsVisible)
        {
            window.Show(main);
            main.PositionChanged += OwnerMoved;
        }
    }

    /// <summary>Hides it; it shows again, where the viewport then is, on the next <see cref="Place"/>.</summary>
    public void Hide()
    {
        if (owner is not null)
            owner.PositionChanged -= OwnerMoved;
        if (window?.IsVisible == true)
            window.Hide();
        owner = null;
    }

    /// <summary>Closes it for good, with the viewer.</summary>
    public void Close()
    {
        wanted = false;
        Hide();
        window?.Close();
        window = null;
    }

    private void OwnerMoved(object? sender, PixelPointEventArgs e) => Place();

    private Window Create()
    {
        var created = new Window
        {
            WindowDecorations = WindowDecorations.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            CanResize = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Background = Brushes.Transparent,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            Content = content,
        };
        if (OperatingSystem.IsWindows())
        {
            // Never activated, even when clicked, and not in Alt+Tab.
            Win32Properties.AddWindowStylesCallback(created, (style, exStyle) => (style, exStyle | WsExNoActivate | WsExToolWindow));
            // Where nothing of it is, the pointer goes through to the viewport, which is the window under it on this
            // thread.
            Win32Properties.AddWndProcHookCallback(created, (nint hwnd, uint message, nint wParam, nint lParam, ref bool handled) =>
            {
                if (message != WmNcHitTest)
                    return 0;
                var screen = new PixelPoint((short)(lParam.ToInt64() & 0xFFFF), (short)((lParam.ToInt64() >> 16) & 0xFFFF));
                if (Covers(created.PointToClient(screen)))
                    return 0;
                handled = true;
                return HtTransparent;
            });
        }
        return created;
    }

    /// <summary>Whether a point in its window is on something of it the pointer can use, not its empty parts.</summary>
    internal bool Covers(Point point) =>
        window?.InputHitTest(point) is Visual hit && (hit == content || content.IsVisualAncestorOf(hit));
}
