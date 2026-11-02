using Avalonia;
using Avalonia.Controls.Platform;

namespace HikariHopper;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
    {
        // Qt Quick cascades a submenu after hovering its item for 225 ms (SUBMENU_DELAY).
        DefaultMenuInteractionHandler.MenuShowDelay = TimeSpan.FromMilliseconds(225);
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // Qt Quick popups (menus, combo box lists) are items inside their window, clipped to it.
            .With(new Win32PlatformOptions { OverlayPopups = true })
            .LogToTrace();
    }
}
