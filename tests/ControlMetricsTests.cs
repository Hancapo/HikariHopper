using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
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

    private static Color Pixel(Window window, int x, int y) => TestPixels.At(window, x, y);

    private static Control TopLeft(Control control)
    {
        control.HorizontalAlignment = HorizontalAlignment.Left;
        control.VerticalAlignment = VerticalAlignment.Top;
        return control;
    }

    [AvaloniaFact]
    public void ChromeToolButtonHasQmlImplicitSizeAndVariantFills()
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
    }

    [AvaloniaFact]
    public void SelectedSegmentIsAPlainRaisedFill()
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
    }

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
    public void DragBadgeShowsTheTotalCount()
    {
        Control badge = DragCountBadge.Badge(2, 3);
        Window window = Show(new Panel { Children = { TopLeft(badge) } }, 80, 60);
        Assert.Equal("5", badge.GetVisualDescendants().OfType<TextBlock>().Single().Text);
        Assert.Equal("file-stack", badge.GetVisualDescendants().OfType<LucideIcon>().Single().IconName);
        Assert.Equal("Extracting 2 folders, 3 files", Avalonia.Automation.AutomationProperties.GetName(badge));
        window.Close();
    }

    [AvaloniaFact]
    public void DragCursorIsTheArrowWithTheBadgeBesideIt()
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
    }

    [AvaloniaFact]
    public void TheTitleBarTellsWindowsWhatItsPartsAre()
    {
        var bar = new WindowTitleBar { VerticalAlignment = VerticalAlignment.Top };
        Window window = Show(bar, 400, 60);
        // The title area is the window's caption (moving, snapping and double-click maximizing it), and the buttons
        // are its minimize, maximize and close buttons: Windows 11 offers snap layouts on the maximize button.
        Assert.Equal(WindowDecorationsElementRole.TitleBar, WindowDecorationProperties.GetElementRole(bar.FindControl<Panel>("DragArea")!));
        // What sits after the emblem, such as the explorer's menus, takes its own clicks over the caption.
        Assert.Equal(WindowDecorationsElementRole.User, WindowDecorationProperties.GetElementRole(bar.FindControl<ContentPresenter>("LeadingHost")!));
        Assert.Equal(WindowDecorationsElementRole.MinimizeButton, WindowDecorationProperties.GetElementRole(bar.MinimizeButton));
        Assert.Equal(WindowDecorationsElementRole.MaximizeButton, WindowDecorationProperties.GetElementRole(bar.MaximizeButton));
        Assert.Equal(WindowDecorationsElementRole.CloseButton, WindowDecorationProperties.GetElementRole(bar.CloseButton));
        // The buttons act through their own clicks, which Avalonia sends them from Windows' non-client input.
        bar.MaximizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(WindowState.Maximized, window.WindowState);
        bar.MaximizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(WindowState.Normal, window.WindowState);
        window.Close();
    }

    [AvaloniaFact]
    public void TheMainWindowIsANativeWindowItsTitleBarCovers()
    {
        var window = new MainWindow();
        // Windows keeps the frame (resizing, rounded corners, snap layouts, taskbar minimizing and its animations);
        // the client area covers it, and Avalonia draws no decorations of its own over the app's title bar.
        Assert.True(window.ExtendClientAreaToDecorationsHint);
        Assert.Equal(WindowDecorations.Full, window.WindowDecorations);
        Assert.Equal(T.MainTitleHeight, window.ExtendClientAreaTitleBarHeightHint);
        Assert.Same(Application.Current!.FindResource("NoDrawnDecorations"), window.WindowDecorationsTheme);
        var splash = new SplashScreen();
        Assert.True(splash.ExtendClientAreaToDecorationsHint);
        Assert.Same(window.WindowDecorationsTheme, splash.WindowDecorationsTheme);
    }

    [AvaloniaFact]
    public void SheetsHaveAHeadingACloseMarkAndAFooter()
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
    }

    [AvaloniaFact]
    public void AboutSaysWhatItIsAndLinksToTheProject()
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
    }

    private static int Distance(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);

    [AvaloniaFact]
    public void EveryWindowCarriesTheAppIcon()
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
    }

    [AvaloniaFact]
    public void QuietScrollBarThumbIsFourIdleEightActiveAndHiddenWhenContentFits()
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
    }

    [AvaloniaFact]
    public void TitleRowButtonsShowFocusOnlyFromTheKeyboard()
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
    }

    [AvaloniaFact]
    public void SubmenusOpenClearOfTheMenuWithTheirFirstRowLevel()
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
    }

    [AvaloniaFact]
    public void FlatTextFieldPaddingFollowsIconAndKeyCap()
    {
        var field = new FlatTextField();
        Assert.Equal(new Thickness(10, 0, 10, 0), field.Padding);
        field.LeadingIcon = "search";
        field.KeyCap = "F3";
        Assert.Equal(new Thickness(32, 0, 40, 0), field.Padding);
        field.Text = "texture_01";
        Assert.Equal(10, field.CaretIndex);                               // Qt moves the cursor to the end
    }

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
    public void EveryVendoredIconParsesAndEveryMappedNameExists()
    {
        foreach ((string name, IReadOnlyList<string> paths) in LucideIcons.Paths)
        {
            // A filled part (an icon's shadow) is marked so.
            foreach (string data in paths)
                Assert.True(Geometry.Parse(data.StartsWith("fill:", StringComparison.Ordinal) ? data[5..] : data).Bounds.Width >= 0, name);
        }
        foreach (string name in ChromeIcon.Names.Values)
            Assert.True(LucideIcons.Paths.ContainsKey(name), name);
        foreach (string kind in new[] { "RPF Package", "Application", "Ped Definitions", "File" })
            Assert.True(LucideIcons.Paths.ContainsKey(FileGlyph.Resolve("file", kind).Icon), kind);
    }

    [AvaloniaFact]
    public void WindowsHaveTheQmlGeometry()
    {
        var main = new MainWindow();
        Assert.Equal((1360.0, 840.0, 940.0, 580.0), (main.Width, main.Height, main.MinWidth, main.MinHeight));
        Assert.Equal("HikariHopper — Modding Toolkit", main.Title);
        Assert.Equal("HikariHopper — Grand Theft Auto V", MainWindow.TitleFor(true, "Grand Theft Auto V"));
        Assert.Equal(WindowDecorations.Full, main.WindowDecorations);

        var splash = new SplashScreen();
        Assert.Equal((720.0, 400.0), (splash.Width, splash.Height));
        Assert.Equal(("Starting…", "BOOT"), (splash.StartupMessage, splash.StartupPhase));
        Assert.True(splash.Topmost);

    }

    [AvaloniaFact]
    public void SheetsAreSizedWithTheWindowWithinTheirBounds()
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
        Assert.Equal(new Size(1033, 720), ModalOverlay.SizeFor(shortcuts, new Size(1360, 800)));
        // One that fits its content is as tall as that, and no taller than its share when it holds more.
        Assert.True(shortcuts.FitsContent);
        shortcuts.FittedHeight = 500;
        Assert.Equal(new Size(1033, 500), ModalOverlay.SizeFor(shortcuts, new Size(1360, 800)));
        shortcuts.FittedHeight = 2000;
        Assert.Equal(new Size(1033, 720), ModalOverlay.SizeFor(shortcuts, new Size(1360, 800)));
        // The others keep their share whatever they hold.
        about.FittedHeight = 200;
        Assert.Equal(new Size(540, 440), ModalOverlay.SizeFor(about, new Size(1360, 800)));
        // A small dialog keeps its own size.
        var fixedDialog = new Border { Width = 300, Height = 200 };
        Assert.Equal(new Size(300, 200), ModalOverlay.SizeFor(fixedDialog, new Size(1360, 800)));
    }

    [AvaloniaFact]
    public void SettingsKeepsItsGroupsInASidebarAndItsPageSixteenPixelsIn()
    {
        var settings = new SettingsDialog();
        Window window = Show(settings, 880, 640);
        Point At(Visual part) => part.TranslatePoint(default, settings)!.Value;
        // The sidebar runs from under the header to the footer, its groups 16 px down.
        Assert.Equal(T.SheetHeaderHeight + 1, At(settings.Sidebar).Y, 0.5);
        Assert.Equal(settings.Bounds.Height - T.DialogFooterHeight - 1, At(settings.Sidebar).Y + settings.Sidebar.Bounds.Height, 0.5);
        Assert.Equal(16, At(settings.GamesGroup).Y - At(settings.Sidebar).Y, 0.5);
        // The page's cards, 16 px from the sidebar and from the sheet's side.
        Control card = settings.GameRows.Children.OfType<SettingsCard>().First();
        Assert.Equal(16, At(card).X - (At(settings.Sidebar).X + settings.Sidebar.Bounds.Width), 0.5);
        Assert.Equal(16, settings.Bounds.Width - 1 - (At(card).X + card.Bounds.Width), 0.5);
        window.Close();
    }

    [AvaloniaFact]
    public void TheKeyboardReferenceListsEveryAreaInColumnsThatStartWhereOneDoes()
    {
        var shortcuts = new ShortcutsDialog();
        Assert.Equal(["FILE", "GO", "EDIT", "3D VIEWER", "TEXTURE EDITOR", "TEXT VIEWER", "IMAGE VIEWER"], shortcuts.Groups.Select(group => group.Name));
        // The explorer's own, the ones the list lacked, and the viewers' and the editor's.
        (string Key, string Action)[] In(string name) => [.. shortcuts.Groups.Single(group => group.Name == name).Keys.Select(entry => (entry.Key, entry.Action))];
        Assert.Contains(("F2", "Rename"), In("EDIT"));
        Assert.Contains(("Alt+Enter", "Properties"), In("EDIT"));
        Assert.Contains(("Ctrl+V", "Paste"), In("EDIT"));
        Assert.Contains(("Ctrl+L · Alt+D", "Type or paste an address"), In("GO"));
        Assert.Equal(["P", "1 – 4", "H", "T", "M", "X", "B", "K", "L", "V", "F", "W A S D · Q E"], In("3D VIEWER").Select(entry => entry.Key));
        Assert.Contains(("Ctrl+E", "Extract as DDS"), In("TEXTURE EDITOR"));
        Assert.Contains(("Alt+Z", "Word wrap"), In("TEXT VIEWER"));
        Assert.Contains(("1", "Actual size"), In("IMAGE VIEWER"));

        // Three columns where there is room, fewer in a narrower sheet, each starting where a part of the app does.
        Assert.Equal(3, ShortcutColumns.ColumnsFor(1000));
        Assert.Equal(2, ShortcutColumns.ColumnsFor(700));
        Assert.Equal(1, ShortcutColumns.ColumnsFor(500));
        double[] heights = [128, 172, 216, 260, 282, 150, 84];
        bool[] starts = [false, false, false, true, false, true, false];
        Assert.Equal([3, 2, 2], ShortcutColumns.Partition(heights, starts, 3, 25));
        Assert.Equal([3, 4], ShortcutColumns.Partition(heights, starts, 2, 25));
        Assert.Equal([7], ShortcutColumns.Partition(heights, starts, 1, 25));
        // No more columns than there are places to start one.
        Assert.Equal([3, 2, 2], ShortcutColumns.Partition(heights, starts, 5, 25));

        // Laid out: the explorer's groups, then the 3D viewer's and the texture editor's, then the text and image viewers'.
        Window window = Show(shortcuts, 1040, 760);
        Assert.Equal([3, 2, 2], shortcuts.Columns.Split);
        Control[] groups = [.. shortcuts.Columns.Children.Where(child => child is StackPanel)];
        Assert.Equal(7, groups.Length);
        Assert.Equal(0, groups[0].Bounds.Y);
        Assert.Equal(0, groups[3].Bounds.Y);
        Assert.Equal(0, groups[5].Bounds.Y);
        Assert.True(groups[3].Bounds.X >= groups[0].Bounds.Right + ShortcutColumns.ColumnGap);
        Assert.True(groups[5].Bounds.X >= groups[3].Bounds.Right + ShortcutColumns.ColumnGap);
        // A rule between two groups of a column, none over a column's first.
        DottedRule[] rules = [.. shortcuts.Columns.Children.OfType<DottedRule>()];
        Assert.Equal(6, rules.Length);
        Assert.Equal([true, true, false, true, false, true], rules.Select(rule => rule.Bounds.Width > 0));
        window.Close();
    }

    [AvaloniaFact]
    public void GamePathRowKeepsItsSizeWhateverItsFolder()
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
    }

    [AvaloniaFact]
    public void GamePathRowReportsDetection()
    {
        var row = new GamePathRow { Title = "Enhanced", Edition = Core.GameSettings.EnhancedEdition, ExpectedExecutable = "GTA5_Enhanced.exe" };
        Assert.Equal("Not set", row.StatusMessage);
        Assert.Equal("folder", row.BrowseButton.IconKind);
        Assert.Equal("Choose folder…", ToolTip.GetTip(row.BrowseButton));
        Assert.False(row.ClearButton.IsEnabled);
        Assert.True(row.ClearButton.IsVisible);
        Assert.True(row.Mark.Enhanced);
        row.GamePath = @"C:\Nope";
        Assert.Equal("GTA5_Enhanced.exe isn't in this folder", row.StatusMessage);
        Assert.Equal(T.Warning, row.StatusText.Foreground);
        Assert.Equal("Change folder…", ToolTip.GetTip(row.BrowseButton));
        Assert.True(row.ClearButton.IsEnabled);
        row.PathValid = true;
        Assert.Equal("\u2713 GTA5_Enhanced.exe found", row.StatusMessage);
        Assert.Equal(T.Ok, row.StatusText.Foreground);
        // The clear mark commits an empty folder.
        string? committed = null;
        row.PathCommitted += (_, path) => committed = path;
        row.ClearButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("", committed);
    }
}
