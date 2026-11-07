using System.Runtime.InteropServices;
using Avalonia.Controls;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>
/// Gives an Avalonia window the native shape Qt gave <c>Qt.FramelessWindowHint</c> windows on
/// Windows: a plain <c>WS_POPUP | WS_CLIPSIBLINGS | WS_CLIPCHILDREN</c> window with no caption.
/// One that can be resized also has a sizing frame, a system menu and minimize and maximize boxes,
/// which Windows 11 needs before it offers snap layouts on the maximize button
/// (<see cref="SnapLayoutHook"/>); the frame takes no room, as the client area is the whole window.
/// On Windows 11 the window manager also rounds its corners and draws the blue piping along the curve.
/// </summary>
/// <remarks>
/// Moving and resizing still go through the window manager (<see cref="Window.BeginMoveDrag"/>
/// sends the same <c>WM_SYSCOMMAND SC_MOVE|HTCAPTION</c> as Qt's <c>startSystemMove()</c>). Windows
/// places a maximized window with a sizing frame past the monitor's edges by the frame's width, so
/// its client area is then the monitor's working area. Where DWM cannot round the window (Windows 10,
/// the headless platform) it keeps square corners; no border is drawn either way.
/// </remarks>
public static class QtFramelessWindow
{
    private const uint WsPopup = 0x80000000;
    private const uint WsClipSiblings = 0x04000000;
    private const uint WsClipChildren = 0x02000000;
    private const uint WsVisible = 0x10000000;
    private const uint WsMinimize = 0x20000000;
    private const uint WsMaximize = 0x01000000;
    private const uint WsDisabled = 0x08000000;
    private const uint WsExWindowEdge = 0x00000100;
    private const uint WsMaximizeBox = 0x00010000;
    private const uint WsMinimizeBox = 0x00020000;
    private const uint WsThickFrame = 0x00040000;
    private const uint WsSysMenu = 0x00080000;
    private const uint NcCalcSize = 0x0083;
    private const uint MonitorDefaultToNearest = 2;

    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwcpRound = 2;
    // DWMWA_COLOR_NONE: no border at all, where Windows would draw one in the accent colour.
    private const int DwmColorNone = unchecked((int)0xFFFFFFFE);

    /// <summary>Drops the platform decoration and installs the Qt window style.</summary>
    public static void Apply(Window window)
    {
        window.WindowDecorations = WindowDecorations.None;
        window.ExtendClientAreaToDecorationsHint = false;
        if (!OperatingSystem.IsWindows())
            return;
        Win32Properties.AddWindowStylesCallback(window, (style, exStyle) =>
        {
            uint state = style & (WsVisible | WsMinimize | WsMaximize | WsDisabled);
            // Windows 11 offers snap layouts only on a window with a sizing frame, a system menu and a maximize box.
            // None of them shows: there's no caption to draw boxes on, and the frame takes no room (below).
            uint sizable = window.CanResize ? WsThickFrame | WsSysMenu | WsMinimizeBox | WsMaximizeBox : 0;
            return (WsPopup | WsClipSiblings | WsClipChildren | sizable | state, exStyle & ~WsExWindowEdge);
        });
        Win32Properties.AddWndProcHookCallback(window, ClientArea);
        window.Opened += (_, _) =>
        {
            if (window.TryGetPlatformHandle()?.Handle is { } handle && handle != 0)
                RoundCorners(handle);
        };
    }

    /// <summary>
    /// The client area is the whole window, so a sizing frame takes no room; when maximized, Windows has put the
    /// window past the monitor's edges by the frame's width, and the client area is the monitor's working area.
    /// </summary>
    private static nint ClientArea(nint hwnd, uint message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != NcCalcSize || (GetWindowLongW(hwnd, -16) & WsThickFrame) == 0)
            return 0;
        handled = true;
        // With wParam set, lParam is an NCCALCSIZE_PARAMS whose first rectangle becomes the client area; without, a
        // RECT that does. Left alone, either is the window's own rectangle.
        if (wParam != 0 && IsZoomed(hwnd))
        {
            var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfoW(MonitorFromWindow(hwnd, MonitorDefaultToNearest), ref monitor))
                Marshal.StructureToPtr(monitor.Work, lParam, false);
        }
        return 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLongW(nint hwnd, int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);

    /// <summary>
    /// Asks DWM for rounded corners and no border; false where the attributes are not supported (before
    /// Windows 11 build 22000).
    /// </summary>
    public static bool RoundCorners(nint handle)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            return false;
        int corner = DwmwcpRound;
        int none = DwmColorNone;
        try
        {
            return DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref corner, sizeof(int)) >= 0
                && DwmSetWindowAttribute(handle, DwmwaBorderColor, ref none, sizeof(int)) >= 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
