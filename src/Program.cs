using Avalonia;
using Avalonia.Controls.Platform;
using HikariHopper.Core;
using HikariHopper.Infrastructure;
using System.Runtime.InteropServices;

namespace HikariHopper;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // First of all: an update being installed runs the app with Velopack's arguments, which this answers and exits.
        Velopack.VelopackApp.Build().Run();
        if (args is not ["--self-test", .. var rest])
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        // A WinExe has no console of its own; report to the terminal it was started from.
        AttachConsole(AttachParentProcess);
        return SelfTest.Run(rest is [var root, ..] ? root : GameSettings.ConfiguredGameRootToOpen(), Console.Out);
    }

    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int processId);

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
