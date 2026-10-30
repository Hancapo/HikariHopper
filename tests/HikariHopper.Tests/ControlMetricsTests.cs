using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HikariHopper.Infrastructure;
using HikariHopper.Views;
using HikariHopper.Views.Controls;
using T = HikariHopper.Ui.Theme;

namespace HikariHopper.Tests;

/// <summary>Key sizes, colours and behaviours of the ported shared controls, checked headlessly.</summary>
public sealed class ControlMetricsTests
{
    private static Window Show(Control content, double width, double height, IBrush? background = null)
    {
        var window = new Window { Width = width, Height = height, Content = content, Background = background ?? T.AppBg };
        window.Show();
        Pump();
        return window;
    }

    private static void Pump()
    {
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    private static Color Pixel(Window window, int x, int y)
    {
        using WriteableBitmap frame = window.CaptureRenderedFrame()!;
        using ILockedFramebuffer buffer = frame.Lock();
        var bytes = new byte[4];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address + y * buffer.RowBytes + x * 4, bytes, 0, 4);
        return buffer.Format == Avalonia.Platform.PixelFormat.Rgba8888
            ? Color.FromArgb(bytes[3], bytes[0], bytes[1], bytes[2])
            : Color.FromArgb(bytes[3], bytes[2], bytes[1], bytes[0]);
    }

    private static Control TopLeft(Control control)
    {
        control.HorizontalAlignment = HorizontalAlignment.Left;
        control.VerticalAlignment = VerticalAlignment.Top;
        return control;
    }

    [AvaloniaFact]
    public void ChromeToolButtonHasQmlImplicitSizeAndVariantFills() => UiThread.Run(() =>
    {
        var ghost = (ChromeToolButton)TopLeft(new ChromeToolButton { Text = "A very long label that does not grow it" });
        var raised = (ChromeToolButton)TopLeft(new ChromeToolButton { Raised = true, Text = "Close", Margin = new Thickness(0, 40, 0, 0) });
        var primary = (ChromeToolButton)TopLeft(new ChromeToolButton { Primary = true, Text = "Open", Margin = new Thickness(0, 80, 0, 0) });
        var panel = new Panel { Children = { ghost, raised, primary } };
        Window window = Show(panel, 200, 120, T.ChromeBg);

        Assert.Equal(new Size(32, 28), ghost.Bounds.Size);
        Assert.Equal(T.ChromeBg.Color, Pixel(window, 3, 25));            // ghost: transparent at rest
        Assert.Equal(T.BorderHard.Color, Pixel(window, 0, 40));          // 1px hard border
        Assert.Equal(T.ChromeRaised.Color, Pixel(window, 3, 65));        // raised fill
        Assert.Equal(T.Selection.Color, Pixel(window, 4, 94));           // primary fill
        Assert.Equal(T.SelectionText, primary.CurrentForeground);

        window.MouseMove(new Point(16, 94));
        Pump();
        Assert.Equal(T.Lighter(T.Selection.Color, 1.11), Pixel(window, 4, 100));
        window.MouseDown(new Point(16, 94), MouseButton.Left);
        Pump();
        Assert.Equal(T.Darker(T.Selection.Color, 1.12), Pixel(window, 4, 100));
        window.MouseUp(new Point(16, 94), MouseButton.Left);
        window.Close();
    });

    [AvaloniaFact]
    public void WindowControlButtonDrawsDividedCellAndPixelMarks() => UiThread.Run(() =>
    {
        var close = (WindowControlButton)TopLeft(new WindowControlButton { Kind = "close" });
        Window window = Show(new Panel { Background = T.ChromeRaised, Children = { close } }, 34, 30);

        Assert.Equal(new Size(34, 30), close.Bounds.Size);
        Assert.Equal(T.BorderHard.Color, Pixel(window, 0, 15));          // left divider
        Assert.Equal(T.TextDim.Color, Pixel(window, 16, 14));            // centre of the X
        Assert.Equal(T.TextDim.Color, Pixel(window, 13, 11));            // aliased 45° stroke ends
        Assert.Equal(T.ChromeRaised.Color, Pixel(window, 12, 11));

        window.MouseMove(new Point(17, 15));
        Pump();
        Assert.Equal(T.Text.Color, Pixel(window, 16, 14));               // hover brightens only the mark
        Assert.Equal(T.ChromeRaised.Color, Pixel(window, 5, 5));
        window.Close();
    });

    [AvaloniaFact]
    public void WindowTitleBarIsThirtyPixelsAndTwentyFourInToolWindows() => UiThread.Run(() =>
    {
        var bar = new WindowTitleBar { VerticalAlignment = VerticalAlignment.Top };
        Window window = Show(bar, 400, 60);
        Assert.Equal(T.WindowTitleHeight, bar.Bounds.Height);
        Assert.Equal(34, bar.CloseButton.Bounds.Width);
        Assert.Equal(T.Brass.Color, Pixel(window, 9, 9));                // emblem outline
        Assert.Equal(T.Brass.Color, Pixel(window, 11, 11));              // 3x3 fill at (2,2)
        Assert.Equal(T.ChromeRaised.Color, Pixel(window, 14, 14));
        window.Close();

        var about = new AboutWindow();
        about.Show();
        Pump();
        WindowTitleBar toolBar = about.GetVisualDescendants().OfType<WindowTitleBar>().Single();
        Assert.Equal(T.HeaderHeight, toolBar.Bounds.Height);
        Assert.False(toolBar.MinimizeButton.IsVisible);
        Assert.False(toolBar.MaximizeButton.IsVisible);
        about.Close();
    });

    [AvaloniaFact]
    public void QuietScrollBarThumbIsFourIdleEightActiveAndHiddenWhenContentFits() => UiThread.Run(() =>
    {
        var rows = new StackPanel();
        for (int i = 0; i < 60; i++)
            rows.Children.Add(new Border { Height = 26 });
        var viewer = new ScrollViewer
        {
            Content = rows,
            Theme = (Avalonia.Styling.ControlTheme)Application.Current!.FindResource("QuietScrollViewer")!,
        };
        Window window = Show(viewer, 160, 200);
        QuietScrollBar bar = viewer.GetVisualDescendants().OfType<QuietScrollBar>().Single();

        Assert.Equal(T.ScrollbarWidth, bar.Bounds.Width);
        Assert.Equal(new Rect(4, 0, 4, 28), bar.ThumbRect);              // 200/1560 of 200px < 28px minimum
        Assert.Equal(T.Guide.Color, Pixel(window, 153, 10));
        window.MouseMove(new Point(154, 100));
        Pump();
        Assert.Equal(new Rect(2, 0, 8, 28), bar.ThumbRect);
        Assert.Equal(T.InsetBg.Color, Pixel(window, 150, 150));          // gutter well while hovered
        Assert.Equal(T.BorderHard.Color, Pixel(window, 148, 150));

        // Qt: a press outside the thumb grabs it by its centre; releasing there scrolls.
        window.MouseDown(new Point(154, 150), MouseButton.Left);
        window.MouseUp(new Point(154, 150), MouseButton.Left);
        Pump();
        Assert.Equal(136, bar.ThumbRect.Y);
        window.Close();

        Window fits = Show(new ScrollViewer
        {
            Content = new Border { Height = 50 },
            Theme = (Avalonia.Styling.ControlTheme)Application.Current!.FindResource("QuietScrollViewer")!,
        }, 160, 200);
        Assert.Equal(T.AppBg.Color, Pixel(fits, 153, 10));
        fits.Close();
    });

    [AvaloniaFact]
    public void MenuBarButtonIsContentWidthPlusPadding() => UiThread.Run(() =>
    {
        var file = (MenuBarButton)TopLeft(new MenuBarButton { Text = "File" });
        Window window = Show(file, 100, 40, T.ChromeBg);
        // Archivo 12 "File" on Qt's whole-pixel advances: 8 + 3 + 3 + 7 = 20, plus 12px each side.
        Assert.Equal(new Size(44, T.MenuHeight), file.Bounds.Size);
        file.MenuOpen = true;
        Pump();
        Assert.Equal(T.ChromeRaised.Color, Pixel(window, 2, 25));        // the open seam joins the popup
        window.Close();
    });

    [AvaloniaFact]
    public void RetroMenuIsFixedWidthWithTwentyFivePixelRows() => UiThread.Run(() =>
    {
        var root = new Panel { Background = T.ChromeBg };
        Window window = Show(root, 400, 200, T.ChromeBg);
        var item = new RetroMenuItem { Text = "Open RPF archive…", ShortcutText = "Ctrl+Shift+O" };
        var menu = new RetroMenu { Items = { item, new RetroMenuSeparator(), new RetroMenuItem { Text = "Exit" } } };
        menu.Open(root, 1, 25);
        Pump();
        Assert.Equal(232, menu.Bounds.Width);
        Assert.Equal(T.MenuItemHeight, item.Bounds.Height);
        Assert.Equal(T.MenuMinimumWidth, item.Bounds.Width);
        Assert.Equal(4 + 25 + 9 + 25 + 4, menu.Bounds.Height);
        menu.Close();
        window.Close();
    });

    [AvaloniaFact]
    public void FlatTextFieldPaddingFollowsIconAndKeyCap() => UiThread.Run(() =>
    {
        var field = new FlatTextField();
        Assert.Equal(new Thickness(10, 0, 10, 0), field.Padding);
        field.LeadingIcon = "search";
        field.KeyCap = "F3";
        Assert.Equal(new Thickness(28, 0, 40, 0), field.Padding);
        field.Text = "texture_01";
        Assert.Equal(10, field.CaretIndex);                               // Qt moves the cursor to the end
    });

    [AvaloniaFact]
    public void FileGlyphResolvesTypeInks()
    {
        Assert.Equal(("folder", "folder"), FileGlyph.Resolve("root", "Drawable"));
        Assert.Equal(("package", "container"), FileGlyph.Resolve("archive", ""));
        Assert.Equal(("puzzle", "code"), FileGlyph.Resolve("file", "Application Extension"));
        Assert.Equal(("book-image", "asset"), FileGlyph.Resolve("file", "Texture Dictionary"));
        Assert.Equal(("file", "data"), FileGlyph.Resolve("file", "Something new"));
        Assert.Same(T.InkCode, FileGlyph.TintFor("code", false));
        Assert.Same(T.SelectionText, FileGlyph.TintFor("code", true));
    }

    [AvaloniaFact]
    public void EveryVendoredIconParsesAndEveryMappedNameExists() => UiThread.Run(() =>
    {
        foreach ((string name, IReadOnlyList<string> paths) in LucideIcons.Paths)
        {
            foreach (string data in paths)
                Assert.True(Geometry.Parse(data).Bounds.Width >= 0, name);
        }
        foreach (string name in ChromeIcon.Names.Values)
            Assert.True(LucideIcons.Paths.ContainsKey(name), name);
        foreach (string kind in new[] { "RPF Package", "Application", "Ped Definitions", "File" })
            Assert.True(LucideIcons.Paths.ContainsKey(FileGlyph.Resolve("file", kind).Icon), kind);
    });

    [AvaloniaFact]
    public void TextUsesQtWholePixelMetrics() => UiThread.Run(() =>
    {
        var label = new TextBlock { Text = "Repair alpha edges", FontFamily = T.UiFont, FontSize = 12 };
        var mono = new TextBlock { Text = "FILE", FontFamily = T.MonoFont, FontSize = 9, FontWeight = FontWeight.Bold };
        Window window = Show(new StackPanel { Children = { TopLeft(label), TopLeft(mono) } }, 300, 100);
        Assert.Equal(108, label.Bounds.Width);                            // design width 102.4
        Assert.Equal(14, label.Bounds.Height);
        Assert.Equal(13, mono.Bounds.Height);                             // round(10.08) + round(3.25)
        Assert.Equal(24, QtTextMetrics.LineHeight(T.MonoFont, 16, FontWeight.Bold));
        Assert.Equal(40, QtTextMetrics.LineHeight(T.UiFont, 36, FontWeight.Bold));
        window.Close();
    });

    [AvaloniaFact]
    public void WindowsHaveTheQmlGeometry() => UiThread.Run(() =>
    {
        var main = new MainWindow();
        Assert.Equal((1360.0, 840.0, 940.0, 580.0), (main.Width, main.Height, main.MinWidth, main.MinHeight));
        Assert.Equal("HikariHopper — RPF Explorer", main.Title);
        Assert.Equal("HikariHopper — Grand Theft Auto V", MainWindow.TitleFor(true, "Grand Theft Auto V"));
        Assert.Equal(WindowDecorations.None, main.WindowDecorations);

        var splash = new SplashScreen();
        Assert.Equal((720.0, 400.0), (splash.Width, splash.Height));
        Assert.Equal(("Starting Qt interface…", "BOOT"), (splash.StartupMessage, splash.StartupPhase));
        Assert.True(splash.Topmost);

        Assert.Equal((460.0, 340.0), (new AboutWindow().Width, new AboutWindow().Height));
        Assert.Equal((470.0, 545.0), (new ShortcutsWindow().Width, new ShortcutsWindow().Height));
    });

    [AvaloniaFact]
    public void SettingsListsFutureGamesInPcReleaseOrder() => UiThread.Run(() =>
    {
        var settings = new SettingsWindow(new Core.GamePathSettings());
        Assert.Equal(
            ["Grand Theft Auto III", "Grand Theft Auto: Vice City", "Grand Theft Auto: San Andreas",
             "Bully: Scholarship Edition", "Grand Theft Auto IV", "Red Dead Redemption 2", "Red Dead Redemption"],
            settings.FutureGames);
        Assert.Equal((1080.0, 688.0, 820.0, 620.0), (settings.Width, settings.Height, settings.MinWidth, settings.MinHeight));
    });

    [AvaloniaFact]
    public void GamePathRowReportsDetection() => UiThread.Run(() =>
    {
        var row = new GamePathRow { GameName = "Grand Theft Auto V Enhanced", ExpectedExecutable = "GTA5_Enhanced.exe", Supported = true };
        Assert.Equal("Not configured", row.StatusMessage);
        row.GamePath = @"C:\Nope";
        Assert.Equal("GTA5_Enhanced.exe not found", row.StatusMessage);
        row.PathValid = true;
        Assert.Equal("Executable detected", row.StatusMessage);
        Assert.Equal(62, row.DesiredHeightFor());
        row.Supported = false;
        Assert.Equal(45, row.DesiredHeightFor());
    });
}

internal static class GamePathRowTestExtensions
{
    public static double DesiredHeightFor(this GamePathRow row)
    {
        row.Measure(new Size(800, double.PositiveInfinity));
        return row.DesiredSize.Height;
    }
}
