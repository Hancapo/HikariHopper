using Avalonia;
using Avalonia.Controls.Platform;
using HikariHopper.Core;
using HikariHopper.Infrastructure;

namespace HikariHopper;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) => args is ["--self-test", .. var rest]
        ? SelfTest.Run(rest is [var root, ..] ? root : GameSettings.ConfiguredGameRootToOpen(), Console.Out)
        : BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

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
