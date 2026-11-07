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
        Assert.Equal(T.BorderHard.Color, Pixel(window, 16, 40));         // 1px hard border
        Assert.NotEqual(T.BorderHard.Color, Pixel(window, 0, 40));       // rounded corner
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
    public void SelectedSegmentIsAPlainRaisedFill() => UiThread.Run(() =>
    {
        // Configured like the toolbar's segments (NavigationBar.axaml, ChromeToolButton.segment).
        var segment = (ChromeToolButton)TopLeft(new ChromeToolButton
        {
            Bordered = false, InWell = true, Sunken = true, IconKind = "list",
            Width = 30, Height = 26, CornerRadius = T.InnerCorner,
        });
        Window window = Show(new Panel { Children = { segment } }, 40, 30, T.InsetBg);

        // A raised fill and nothing else: no dark edge along its top.
        Assert.Equal(T.ChromeRaised.Color, Pixel(window, 15, 0));
        Assert.False(segment.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "PART_Bevel").IsVisible);
        window.Close();
    });

    [Theory]
    [InlineData(0, 1, "Extracting 1 file")]
    [InlineData(0, 3, "Extracting 3 files")]
    [InlineData(1, 0, "Extracting 1 folder")]
    [InlineData(2, 5, "Extracting 2 folders, 5 files")]
    public void DragBadgeNamesFoldersAndFiles(int folders, int files, string expected) =>
        Assert.Equal(expected, DragCountBadge.Describe(folders, files));

    [Theory]
    [InlineData(2, 0, "folder")]
    [InlineData(0, 5, "file")]
    [InlineData(2, 3, "file-stack")]
    public void DragBadgeIconFollowsWhatIsDragged(int folders, int files, string icon) =>
        Assert.Equal(icon, DragCountBadge.IconFor(folders, files));

    [AvaloniaFact]
    public void DragBadgeShowsTheTotalCount() => UiThread.Run(() =>
    {
        Control badge = DragCountBadge.Badge(2, 3);
        Window window = Show(new Panel { Children = { TopLeft(badge) } }, 80, 60);
        Assert.Equal("5", badge.GetVisualDescendants().OfType<TextBlock>().Single().Text);
        Assert.Equal("file-stack", badge.GetVisualDescendants().OfType<LucideIcon>().Single().IconName);
        Assert.Equal("Extracting 2 folders, 3 files", Avalonia.Automation.AutomationProperties.GetName(badge));
        window.Close();
    });

    [AvaloniaFact]
    public void DragCursorIsTheArrowWithTheBadgeBesideIt() => UiThread.Run(() =>
    {
        static int Alpha(byte[] pixels, int width, int x, int y) => pixels[(y * width + x) * 4 + 3];
        var (pixels, width, height) = DragCountBadge.Render(DragCountBadge.CursorVisual(2, 3), 1);
        // The arrow's tip is the hot spot at the top-left; the badge sits below and to the right of it.
        Assert.Equal((12 + 44, 16 + 42), (width, height));
        Assert.Equal(255, Alpha(pixels, width, 1, 2));
        Assert.Equal(255, Alpha(pixels, width, 12 + 18, 16 + 18));       // the badge's tile
        Assert.Equal(0, Alpha(pixels, width, width - 1, 0));            // empty corner above the badge
        // Straight alpha, as cursors take it: an opaque white pixel of the arrow's fill stays white.
        int white = (4 * width + 2) * 4;
        Assert.Equal([255, 255, 255, 255], pixels[white..(white + 4)]);

        // Where nothing can be dropped the badge stays opaque, so it reads on any background; its count circle turns neutral.
        var (blocked, _, _) = DragCountBadge.Render(DragCountBadge.CursorVisual(2, 3, blocked: true), 1);
        Assert.Equal(255, Alpha(blocked, width, 12 + 18, 16 + 18));
        int circle = ((16 + 42 - 5) * width + 12 + 44 - 9) * 4;
        Assert.Equal(255, pixels[circle + 3]);
        Assert.NotEqual(pixels[circle..(circle + 3)], blocked[circle..(circle + 3)]);

        var (_, wide, tall) = DragCountBadge.Render(DragCountBadge.CursorVisual(2, 3), 2);
        Assert.Equal((2 * width, 2 * height), (wide, tall));
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
        // The emblem is the app icon: its small design, 12px here, drawn from a 64px bitmap.
        Image emblem = bar.FindControl<Image>("Emblem")!;
        Assert.Equal(new Size(12, 12), emblem.Bounds.Size);
        Assert.Equal(new PixelSize(64, 64), Assert.IsAssignableFrom<Bitmap>(emblem.Source).PixelSize);
        Point at = emblem.TranslatePoint(default, window)!.Value;
        var brass = Color.Parse("#F4C26F");
        bool gold = false;
        for (int y = 0; y < 12; y++)
            for (int x = 0; x < 12; x++)
                gold |= Distance(Pixel(window, (int)at.X + x, (int)at.Y + y), brass) < 40;
        Assert.True(gold, "the emblem shows its brass tile");
        Assert.Equal(T.ChromeRaised.Color, Pixel(window, (int)at.X + 16, (int)at.Y + 6));
        window.Close();
    });

    [AvaloniaFact]
    public void TheMaximizeButtonIsTheWindowsMaximizeButtonForSnapLayouts() => UiThread.Run(() =>
    {
        var bar = new WindowTitleBar { VerticalAlignment = VerticalAlignment.Top };
        Window window = Show(bar, 400, 60);
        SnapLayoutHook hook = bar.SnapLayouts!;
        WindowControlButton maximize = bar.MaximizeButton;
        WindowControlGlyph glyph = maximize.GetVisualDescendants().OfType<WindowControlGlyph>().Single();
        Point middle = maximize.TranslatePoint(new Point(maximize.Bounds.Width / 2, maximize.Bounds.Height / 2), window)!.Value;

        // Windows asks what is under the pointer: the button's area answers HTMAXBUTTON, the rest of the bar is left alone.
        Assert.Equal((true, SnapLayoutHook.MaxButton), hook.Handle(SnapLayoutHook.NcHitTest, 0, middle));
        Assert.Equal((false, (nint)0), hook.Handle(SnapLayoutHook.NcHitTest, 0, new Point(60, 15)));
        Assert.False(hook.Handle(SnapLayoutHook.NcHitTest, 0, bar.CloseButton.TranslatePoint(new Point(17, 15), window)!.Value).Handled);

        // The pointer then comes as non-client messages, which light the button and press it.
        hook.Handle(SnapLayoutHook.NcMouseMove, SnapLayoutHook.MaxButton, null);
        Assert.Equal(T.Text, glyph.Ink);
        Assert.True(hook.Handle(SnapLayoutHook.NcLeftButtonDown, SnapLayoutHook.MaxButton, null).Handled);
        Assert.True(glyph.Pressed);
        Assert.True(hook.Handle(SnapLayoutHook.NcLeftButtonUp, SnapLayoutHook.MaxButton, null).Handled);
        Assert.False(glyph.Pressed);
        Assert.Equal(WindowState.Maximized, window.WindowState);
        hook.Handle(SnapLayoutHook.NcLeftButtonDown, SnapLayoutHook.MaxButton, null);
        hook.Handle(SnapLayoutHook.NcLeftButtonUp, SnapLayoutHook.MaxButton, null);
        Assert.Equal(WindowState.Normal, window.WindowState);
        // A release with no press on the button is no click.
        hook.Handle(SnapLayoutHook.NcLeftButtonUp, SnapLayoutHook.MaxButton, null);
        Assert.Equal(WindowState.Normal, window.WindowState);
        hook.Handle(SnapLayoutHook.NcMouseLeave, 0, null);
        Assert.Equal(T.TextDim, glyph.Ink);

        // Without maximize on offer, there's no maximize button to show snap layouts on.
        window.CanResize = false;
        Assert.False(hook.Handle(SnapLayoutHook.NcHitTest, 0, middle).Handled);
        window.CanResize = true;
        bar.ShowMaximize = false;
        Pump();
        Assert.False(hook.Handle(SnapLayoutHook.NcHitTest, 0, middle).Handled);
        window.Close();
    });

    [AvaloniaFact]
    public void SheetsHaveAHeadingACloseMarkAndAFooter() => UiThread.Run(() =>
    {
        var about = new AboutDialog();
        Window window = Show(about, 520, 460);
        var heading = about.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "PART_Heading");
        Assert.Equal("ABOUT", heading.Text);
        var buttons = about.GetVisualDescendants().OfType<ChromeToolButton>().ToArray();
        Assert.Contains(buttons, button => button.Name == "PART_CloseMark");
        Assert.Contains(buttons, button => button.Name == "PART_CloseButton" && button.Text == "Close");
        // The version comes from the build, not a hard-coded number.
        Assert.Equal(StartPage.AppVersion, about.FindControl<TextBlock>("VersionText")!.Text);
        window.Close();
    });

    [AvaloniaFact]
    public void AboutSaysWhatItIsAndLinksToTheProject() => UiThread.Run(() =>
    {
        var about = new AboutDialog();
        Window window = Show(about, 520, 460);
        // A modding toolkit, with the credits in the footer line.
        Assert.Contains(about.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "A modding toolkit for games");
        Assert.Equal("FiveFury · Avalonia · Archivo, Space Mono, Inter", about.FooterText);
        Assert.Equal(26, about.FindControl<Image>("Wordmark")!.Bounds.Height);

        // GitHub opens the project in the browser.
        var opened = new List<Uri>();
        Action<TopLevel?, Uri> previous = AboutDialog.Launch;
        AboutDialog.Launch = (_, uri) => opened.Add(uri);
        try
        {
            about.FindControl<ChromeToolButton>("GitHubButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        }
        finally
        {
            AboutDialog.Launch = previous;
        }
        Assert.Equal([AboutDialog.ProjectPage], opened);
        Assert.Equal("https://github.com/Hancapo/HikariHopper", AboutDialog.ProjectPage.ToString());
        window.Close();
    });

    private static int Distance(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);

    [AvaloniaFact]
    public void EveryWindowCarriesTheAppIcon() => UiThread.Run(() =>
    {
        // The taskbar and Alt+Tab icon comes from one style, so every window class gets it.
        foreach (Window window in new Window[] { new MainWindow(), new SplashScreen() })
            Assert.NotNull(window.Icon);
        var about = new AboutDialog();
        Window host = Show(about, 520, 460);
        Image icon = about.FindControl<Image>("AppIcon")!;
        Assert.Equal(new PixelSize(256, 256), Assert.IsAssignableFrom<Bitmap>(icon.Source).PixelSize);
        Assert.Equal(new Size(56, 56), icon.Bounds.Size);
        host.Close();

        // The splash wears the lockup: the icon, then the official wordmark at its own proportions.
        var splash = new SplashScreen();
        splash.Show();
        Pump();
        Image wordmark = splash.FindControl<Image>("Wordmark")!;
        var bitmap = Assert.IsAssignableFrom<Bitmap>(wordmark.Source);
        Assert.Equal(38, wordmark.Bounds.Height);
        Assert.Equal(38.0 * bitmap.PixelSize.Width / bitmap.PixelSize.Height, wordmark.Bounds.Width, 1);
        splash.Close();
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
        Assert.Equal(T.Border.Color, Pixel(window, 153, 10));
        Assert.NotEqual(T.Border.Color, Pixel(window, 152, 0));          // rounded end
        window.MouseMove(new Point(154, 100));
        Pump();
        Assert.Equal(new Rect(2, 0, 8, 28), bar.ThumbRect);
        Assert.Equal(T.TextDim.Color, Pixel(window, 151, 14));           // the widened thumb
        Assert.Equal(T.AppBg.Color, Pixel(window, 150, 150));            // no gutter well

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

    [Fact]
    public void Windows11RoundsTheFramelessWindowsCorners()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("DWM is Windows-only.");
        // A hidden native popup window, never shown on screen.
        nint handle = CreateWindowExW(0, "STATIC", "HikariHopper corner probe", 0x80000000, 0, 0, 100, 100, 0, 0, 0, 0);
        Assert.NotEqual(0, handle);
        try
        {
            Assert.Equal(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000), QtFramelessWindow.RoundCorners(handle));
        }
        finally
        {
            DestroyWindow(handle);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern nint CreateWindowExW(int exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint handle);

    [AvaloniaFact]
    public void KeyCapIsARoundedRaisedKey() => UiThread.Run(() =>
    {
        var cap = (KeyCap)TopLeft(new KeyCap { Text = "F3" });
        Window window = Show(cap, 60, 30, T.InsetBg);
        Assert.Equal(18, cap.Bounds.Height);
        Assert.Equal(T.KeyCapCorner, cap.CornerRadius);
        Assert.Equal(T.Border.Color, Pixel(window, 0, 9));                // outer ring
        Assert.Equal(T.BorderHard.Color, Pixel(window, (int)cap.Bounds.Width / 2, 16));  // dark lower edge
        Assert.NotEqual(T.Border.Color, Pixel(window, 0, 0));             // rounded corner
        window.Close();
    });

    [AvaloniaFact]
    public void MenuBarButtonIsContentWidthPlusPadding() => UiThread.Run(() =>
    {
        var file = (MenuBarButton)TopLeft(new MenuBarButton { Text = "File" });
        Window window = Show(file, 100, 40, T.ChromeBg);
        // Archivo 12 "File" on Qt's whole-pixel advances: 8 + 3 + 3 + 7 = 20, plus 12px each side.
        Assert.Equal(new Size(44, T.MenuHeight), file.Bounds.Size);
        Assert.Equal(T.ChromeBg.Color, Pixel(window, 4, 13));            // transparent at rest
        // While its menu is open it is the top of the menu: the menu's surface, outlined on three
        // sides, rounded on top and open at the bottom, where the menu carries on.
        file.MenuOpen = true;
        Pump();
        int bottom = (int)file.Bounds.Height - 1;
        Assert.Equal(T.ChromeRaised.Color, Pixel(window, 4, 13));
        Assert.Equal(T.Border.Color, Pixel(window, 0, 13));               // left outline
        Assert.Equal(T.Border.Color, Pixel(window, 43, 13));              // right outline
        Assert.Equal(T.Border.Color, Pixel(window, 20, 0));               // top outline
        Assert.NotEqual(T.Border.Color, Pixel(window, 0, 0));             // rounded top corner
        Assert.Equal(T.ChromeRaised.Color, Pixel(window, 20, bottom));    // open bottom
        Assert.Equal(T.Border.Color, Pixel(window, 0, bottom));           // square bottom corner
        window.Close();
    });

    [AvaloniaFact]
    public void TitleRowButtonsShowFocusOnlyFromTheKeyboard() => UiThread.Run(() =>
    {
        var file = (MenuBarButton)TopLeft(new MenuBarButton { Text = "File" });
        var close = new WindowControlButton { Kind = "close", HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(60, 0, 0, 0) };
        Window window = Show(new Panel { Children = { file, close } }, 100, 40, T.ChromeBg);

        // No rule under the title is left in the template.
        Assert.DoesNotContain(file.GetVisualDescendants().Concat(close.GetVisualDescendants()), visual => visual is Control { Name: "PART_FocusRule" });

        // Focus from a click (a menu that closed again) shows nothing.
        file.Focus(NavigationMethod.Pointer);
        Pump();
        Assert.True(file.IsFocused);
        Assert.False(file.KeyboardFocused);
        Assert.Equal(T.ChromeBg.Color, Pixel(window, 4, 13));
        for (int x = 14; x < 30; x++)
            Assert.Equal(T.ChromeBg.Color, Pixel(window, x, (int)file.Bounds.Height - 3));

        close.Focus(NavigationMethod.Pointer);
        Pump();
        Assert.False(close.KeyboardFocused);

        // From the keyboard, the title lights the way hover does, and goes dark when focus moves on.
        file.Focus(NavigationMethod.Tab);
        Pump();
        Assert.True(file.KeyboardFocused);
        Assert.Equal(T.HoverChrome.Color, Pixel(window, 4, 13));
        close.Focus(NavigationMethod.Tab);
        Pump();
        Assert.True(close.KeyboardFocused);
        Assert.False(file.KeyboardFocused);
        Assert.Equal(T.ChromeBg.Color, Pixel(window, 4, 13));
        window.Close();
    });

    [AvaloniaFact]
    public void RetroMenuIsFixedWidthWithRowHeightRows() => UiThread.Run(() =>
    {
        var root = new Panel { Background = T.ChromeBg };
        Window window = Show(root, 400, 200, T.ChromeBg);
        var item = new RetroMenuItem { Text = "Open RPF archive…", ShortcutText = "Ctrl+Shift+O" };
        var menu = new RetroMenu { Items = { item, new RetroMenuSeparator(), new RetroMenuItem { Text = "Exit" } } };
        menu.Open(root, 1, 25);
        Pump();
        Assert.Equal(T.MenuWidth, menu.Bounds.Width);
        Assert.Equal(T.MenuItemHeight, item.Bounds.Height);
        Assert.Equal(T.MenuMinimumWidth, item.Bounds.Width);
        Assert.Equal(4 + 26 + 9 + 26 + 4, menu.Bounds.Height);
        // Rows sit inside the frame's 4px padding, the label in the column after the icon cell.
        Assert.Equal(new Point(4, 4), item.TranslatePoint(default, menu));
        menu.Close();
        window.Close();
    });

    [AvaloniaFact]
    public void SubmenusOpenClearOfTheMenuWithTheirFirstRowLevel() => UiThread.Run(() =>
    {
        var root = new Panel { Background = T.ChromeBg };
        Window window = Show(root, 800, 400, T.WindowBg);
        var child = new RetroMenuItem { Text = "Folder", IconName = "folder" };
        var parent = new RetroMenuItem { Text = "New", IconName = "plus", Items = { child } };
        var menu = new RetroMenu { Items = { new RetroMenuItem { Text = "Open" }, parent } };
        menu.Open(root, 20, 20);
        Pump();
        parent.IsSubMenuOpen = true;
        Pump();
        Point menuAt = menu.TranslatePoint(default, window)!.Value;
        Point parentAt = parent.TranslatePoint(default, window)!.Value;
        Point childAt = child.TranslatePoint(default, window)!.Value;
        // 4px between the frames; the submenu's first row level with the row that opened it.
        Assert.Equal(menuAt.X + T.MenuWidth + RetroMenuItem.SubmenuGap + T.MenuPadding, childAt.X);
        Assert.Equal(parentAt.Y, childAt.Y);
        // The gap belongs to the submenu, so a pointer crossing it never leaves the submenu.
        TopLevel host = TopLevel.GetTopLevel(child)!;
        Point gap = child.TranslatePoint(new Point(-T.MenuPadding - RetroMenuItem.SubmenuGap / 2, T.MenuItemHeight / 2), host)!.Value;
        Assert.Equal("PART_SubmenuBridge", Assert.IsAssignableFrom<Control>(host.InputHitTest(gap)).Name);
        parent.IsSubMenuOpen = false;
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
        Assert.Equal(new Thickness(32, 0, 40, 0), field.Padding);
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
        Assert.Equal(("image", "asset"), FileGlyph.Resolve("file", "DDS Texture"));
        Assert.Equal(("image", "asset"), FileGlyph.Resolve("file", "PNG Image"));
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
    public void TextKeepsTheFontsDesignMetrics() => UiThread.Run(() =>
    {
        // Avalonia's own shaping: glyph advances are not rounded to whole pixels.
        var label = new TextBlock { Text = "Repair alpha edges", FontFamily = T.UiFont, FontSize = 12 };
        Window window = Show(new StackPanel { Children = { TopLeft(label) } }, 300, 100);
        Assert.Equal(103, label.Bounds.Width);                            // design width 102.4, laid out on whole pixels
        // Text fields still centre their line on Qt's rounded line box.
        Assert.Equal(24, QtTextMetrics.LineHeight(T.MonoFont, 16, FontWeight.Bold));
        Assert.Equal(40, QtTextMetrics.LineHeight(T.UiFont, 36, FontWeight.Bold));
        window.Close();
    });

    [AvaloniaFact]
    public void WindowsHaveTheQmlGeometry() => UiThread.Run(() =>
    {
        var main = new MainWindow();
        Assert.Equal((1360.0, 840.0, 940.0, 580.0), (main.Width, main.Height, main.MinWidth, main.MinHeight));
        Assert.Equal("HikariHopper — Modding Toolkit", main.Title);
        Assert.Equal("HikariHopper — Grand Theft Auto V", MainWindow.TitleFor(true, "Grand Theft Auto V"));
        Assert.Equal(WindowDecorations.None, main.WindowDecorations);

        var splash = new SplashScreen();
        Assert.Equal((720.0, 400.0), (splash.Width, splash.Height));
        Assert.Equal(("Starting Qt interface…", "BOOT"), (splash.StartupMessage, splash.StartupPhase));
        Assert.True(splash.Topmost);

    });

    [AvaloniaFact]
    public void SheetsAreSizedWithTheWindowWithinTheirBounds() => UiThread.Run(() =>
    {
        // Their share of the window, within their least and most, never closer than 24px to an edge.
        var settings = new SettingsDialog();
        Assert.Equal(new Size(816, 592), ModalOverlay.SizeFor(settings, new Size(1360, 800)));
        Assert.Equal(new Size(880, 640), ModalOverlay.SizeFor(settings, new Size(2560, 1400)));
        Assert.Equal(new Size(700, 460), ModalOverlay.SizeFor(settings, new Size(940, 540)));
        Assert.Equal(new Size(552, 352), ModalOverlay.SizeFor(settings, new Size(600, 400)));
        var about = new AboutDialog();
        Assert.Equal(new Size(540, 440), ModalOverlay.SizeFor(about, new Size(1360, 800)));
        Assert.Equal(new Size(440, 380), ModalOverlay.SizeFor(about, new Size(940, 540)));
        var shortcuts = new ShortcutsDialog();
        Assert.Equal(new Size(544, 640), ModalOverlay.SizeFor(shortcuts, new Size(1360, 800)));
        // A small dialog keeps its own size.
        var fixedDialog = new Border { Width = 300, Height = 200 };
        Assert.Equal(new Size(300, 200), ModalOverlay.SizeFor(fixedDialog, new Size(1360, 800)));
    });

    [AvaloniaFact]
    public void GamePathRowKeepsItsSizeWhateverItsFolder() => UiThread.Run(() =>
    {
        // Not set, set and found, set and not found: the same height every time, so the card never moves. In a
        // window, whose layout rounding is what turned the check's taller line into a pixel.
        var row = new GamePathRow { Title = "Enhanced", Edition = Core.GameSettings.EnhancedEdition, ExpectedExecutable = "GTA5_Enhanced.exe" };
        var window = new Window { Width = 520, Height = 200, Content = new StackPanel { Children = { row } } };
        window.Show();
        double Height()
        {
            window.UpdateLayout();
            return row.Bounds.Height;
        }
        double unset = Height();
        row.GamePath = @"D:\Games\Grand Theft Auto V Enhanced";
        row.PathValid = true;
        Assert.Equal(unset, Height());
        row.PathValid = false;
        Assert.Equal(unset, Height());
        window.Close();
    });

    [AvaloniaFact]
    public void GamePathRowReportsDetection() => UiThread.Run(() =>
    {
        var row = new GamePathRow { Title = "Enhanced", Edition = Core.GameSettings.EnhancedEdition, ExpectedExecutable = "GTA5_Enhanced.exe" };
        Assert.Equal("Not set", row.StatusMessage);
        Assert.Equal("folder", row.BrowseButton.IconKind);
        Assert.Equal("Choose folder…", ToolTip.GetTip(row.BrowseButton));
        Assert.False(row.ClearButton.IsVisible);
        Assert.True(row.Mark.Enhanced);
        row.GamePath = @"C:\Nope";
        Assert.Equal("GTA5_Enhanced.exe isn't in this folder", row.StatusMessage);
        Assert.Equal(T.Warning, row.StatusText.Foreground);
        Assert.Equal("Change folder…", ToolTip.GetTip(row.BrowseButton));
        Assert.True(row.ClearButton.IsVisible);
        row.PathValid = true;
        Assert.Equal("\u2713 GTA5_Enhanced.exe found", row.StatusMessage);
        Assert.Equal(T.Ok, row.StatusText.Foreground);
        // The clear mark commits an empty folder.
        string? committed = null;
        row.PathCommitted += (_, path) => committed = path;
        row.ClearButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("", committed);
    });
}
