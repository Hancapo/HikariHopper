using System.Runtime.InteropServices;
using Avalonia.Controls;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>
/// Gives an Avalonia window the native shape Qt gave <c>Qt.FramelessWindowHint</c> windows on
/// Windows: a plain <c>WS_POPUP | WS_CLIPSIBLINGS | WS_CLIPCHILDREN</c> window with no caption,
/// sizing frame or minimize/maximize boxes. On Windows 11 the window manager also rounds its
/// corners and draws the blue piping along the curve.
/// </summary>
/// <remarks>
/// Moving and resizing still go through the window manager (<see cref="Window.BeginMoveDrag"/>
/// sends the same <c>WM_SYSCOMMAND SC_MOVE|HTCAPTION</c> as Qt's <c>startSystemMove()</c>), and
/// Avalonia maximizes a captionless window to the monitor's working area exactly as Qt did, so
/// a maximized window needs no extra insets. Where DWM cannot round the window (Windows 10, the
/// headless platform) it keeps square corners; no border is drawn either way.
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
            return (WsPopup | WsClipSiblings | WsClipChildren | state, exStyle & ~WsExWindowEdge);
        });
        window.Opened += (_, _) =>
        {
            if (window.TryGetPlatformHandle()?.Handle is { } handle && handle != 0)
                RoundCorners(handle);
        };
    }

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
