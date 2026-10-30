using Avalonia;
using Avalonia.Headless;
using HikariHopper.Infrastructure;
using HikariHopper.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace HikariHopper.Tests;

/// <summary>
/// Headless Avalonia with real Skia rendering, so UI tests can read back pixels. Popups render in
/// the window's overlay layer, as they do in the app (Qt Quick popups are items in their window).
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        QtTextShaper.Use(AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false, OverlayPopups = true }));
}
