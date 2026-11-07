using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace HikariHopper.Views.Controls;

/// <summary>
/// Windows 11's snap layouts on an application-drawn maximize button. The flyout opens over whatever part of a window
/// answers <c>WM_NCHITTEST</c> with <c>HTMAXBUTTON</c>, so the button's area answers that, and as Windows then sends
/// the pointer there as non-client messages, those drive the button's hover, press and click instead.
/// </summary>
/// <remarks>
/// Avalonia's own non-client roles apply only to windows whose client area is extended over the platform's
/// decorations, and these windows have none; so the window procedure is hooked directly.
/// </remarks>
internal sealed class SnapLayoutHook : IDisposable
{
    internal const uint NcHitTest = 0x0084;
    internal const uint NcMouseMove = 0x00A0;
    internal const uint NcLeftButtonDown = 0x00A1;
    internal const uint NcLeftButtonUp = 0x00A2;
    internal const uint NcLeftButtonDoubleClick = 0x00A3;
    internal const uint NcMouseLeave = 0x02A2;
    internal const uint MouseMove = 0x0200;
    internal const nint MaxButton = 9;

    private const uint TrackLeave = 0x00000002;
    private const uint TrackNonClient = 0x00000010;

    private readonly Window window;
    private readonly WindowControlButton button;
    private readonly Func<bool> offered;
    private readonly Action toggle;
    private readonly Win32Properties.CustomWndProcHookCallback? callback;
    private bool tracking;

    /// <param name="offered">Whether the window offers maximize now; when it doesn't, the button is left to Avalonia.</param>
    /// <param name="toggle">Maximizes or restores the window, as a click on the button does.</param>
    public SnapLayoutHook(Window window, WindowControlButton button, Func<bool> offered, Action toggle)
    {
        this.window = window;
        this.button = button;
        this.offered = offered;
        this.toggle = toggle;
        if (!OperatingSystem.IsWindows())
            return;
        callback = Procedure;
        Win32Properties.AddWndProcHookCallback(window, callback);
    }

    public void Dispose()
    {
        if (callback is not null)
            Win32Properties.RemoveWndProcHookCallback(window, callback);
        Leave();
    }

    private nint Procedure(nint hwnd, uint message, nint wParam, nint lParam, ref bool handled)
    {
        Point? at = message == NcHitTest ? window.PointToClient(ScreenPoint(lParam)) : null;
        (bool taken, nint result) = Handle(message, wParam, at);
        if (message == NcMouseMove && wParam == MaxButton && !tracking)
            tracking = TrackLeaving(hwnd);
        handled = taken;
        return result;
    }

    /// <summary>
    /// The screen point a mouse message's lParam holds, in physical pixels: two signed 16-bit words, as a monitor left
    /// of or above the main one has negative coordinates.
    /// </summary>
    internal static PixelPoint ScreenPoint(nint lParam) => new((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF));

    /// <summary>
    /// One window message: whether it is taken, and the result. <paramref name="at"/> is the pointer in the window's
    /// client coordinates, for <c>WM_NCHITTEST</c>.
    /// </summary>
    internal (bool Handled, nint Result) Handle(uint message, nint wParam, Point? at)
    {
        switch (message)
        {
            case NcHitTest:
                return at is { } point && Over(point) ? (true, MaxButton) : (false, 0);
            case NcMouseMove:
                if (wParam == MaxButton)
                    button.NonClientHover = true;
                else
                    Leave();
                return (false, 0);
            // Taken, so Windows doesn't track a press on a caption button the window doesn't have.
            case NcLeftButtonDown or NcLeftButtonDoubleClick when wParam == MaxButton:
                button.NonClientPressed = true;
                return (true, 0);
            case NcLeftButtonUp when wParam == MaxButton:
                bool click = button.NonClientPressed;
                button.NonClientPressed = false;
                if (click)
                    toggle();
                return (true, 0);
            case NcLeftButtonUp:
                button.NonClientPressed = false;
                return (false, 0);
            case NcMouseLeave or MouseMove:
                Leave();
                return (false, 0);
        }
        return (false, 0);
    }

    private bool Over(Point point)
    {
        if (!offered() || !button.IsEffectivelyVisible || !button.IsEffectivelyEnabled
            || button.TranslatePoint(default, window) is not { } corner)
            return false;
        return new Rect(corner, button.Bounds.Size).Contains(point);
    }

    private void Leave()
    {
        tracking = false;
        button.NonClientHover = false;
        button.NonClientPressed = false;
    }

    // Asks for WM_NCMOUSELEAVE when the pointer leaves the button, so its hover goes with it.
    private static bool TrackLeaving(nint hwnd)
    {
        var request = new TrackMouseEventRequest
        {
            Size = (uint)Marshal.SizeOf<TrackMouseEventRequest>(),
            Flags = TrackLeave | TrackNonClient,
            Window = hwnd,
        };
        return TrackMouseEvent(ref request);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrackMouseEventRequest
    {
        public uint Size;
        public uint Flags;
        public nint Window;
        public uint HoverTime;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TrackMouseEvent(ref TrackMouseEventRequest request);
}
