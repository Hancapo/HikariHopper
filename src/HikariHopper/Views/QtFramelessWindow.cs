using Avalonia.Controls;

namespace HikariHopper.Views;

/// <summary>
/// Gives an Avalonia window the native shape Qt gave <c>Qt.FramelessWindowHint</c> windows on
/// Windows: a plain <c>WS_POPUP | WS_CLIPSIBLINGS | WS_CLIPCHILDREN</c> window with no caption,
/// sizing frame, minimize/maximize boxes or DWM rounding.
/// </summary>
/// <remarks>
/// Moving and resizing still go through the window manager (<see cref="Window.BeginMoveDrag"/>
/// sends the same <c>WM_SYSCOMMAND SC_MOVE|HTCAPTION</c> as Qt's <c>startSystemMove()</c>), and
/// Avalonia maximizes a captionless window to the monitor's working area exactly as Qt did, so
/// a maximized window needs no extra insets.
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
    }
}
