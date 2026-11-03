using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using HikariHopper.Core;
using HikariHopper.Views;
using HikariHopper.Views.Controls;
using T = HikariHopper.Ui.Theme;

namespace HikariHopper.Tests;

/// <summary>
/// Renders the shared QML-vs-Avalonia comparison scenarios to PNGs. Opt-in: set
/// <c>HH_SCENARIOS</c> to the scenarios.json written by the comparison scripts and
/// <c>HH_RENDER_OUT</c> to an output folder (optionally <c>HH_ONLY</c> to a comma list of names).
/// </summary>
public sealed class RenderScenarios
{
    private const string LegacyPath = @"D:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V";

    [AvaloniaFact]
    public void RenderAll()
    {
        string? scenarios = Environment.GetEnvironmentVariable("HH_SCENARIOS");
        string? output = Environment.GetEnvironmentVariable("HH_RENDER_OUT");
        if (string.IsNullOrEmpty(scenarios) || string.IsNullOrEmpty(output))
            Assert.Skip("Set HH_SCENARIOS and HH_RENDER_OUT to render the comparison scenarios.");
        string[]? only = Environment.GetEnvironmentVariable("HH_ONLY")?.Split(',', StringSplitOptions.RemoveEmptyEntries);
        UiThread.Run(() => RenderAllOnUiThread(scenarios, output, only));
    }

    private static void RenderAllOnUiThread(string scenarios, string output, string[]? only)
    {
        Directory.CreateDirectory(output);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(scenarios));
        var failures = new List<string>();
        foreach (JsonElement scenario in document.RootElement.EnumerateArray())
        {
            string name = scenario.GetProperty("name").GetString()!;
            if (only is not null && !only.Contains(name))
                continue;
            try
            {
                Render(scenario, name, output);
            }
            catch (Exception error)
            {
                failures.Add($"{name}: {error}");
            }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    private static void Render(JsonElement scenario, string name, string output)
    {
        int width = scenario.GetProperty("w").GetInt32();
        int height = scenario.GetProperty("h").GetInt32();
        Window window = Build(name, width, height, out Control? focusTarget, out Action<Window>? call);
        window.Show();
        Pump();
        foreach (JsonElement action in scenario.GetProperty("actions").EnumerateArray())
        {
            string kind = action[0].GetString()!;
            Point At() => new(action[1].GetDouble(), action[2].GetDouble());
            switch (kind)
            {
                case "move":
                    window.MouseMove(At());
                    break;
                case "press":
                    window.MouseDown(At(), MouseButton.Left);
                    break;
                case "release":
                    window.MouseUp(At(), MouseButton.Left);
                    break;
                case "click":
                    window.MouseDown(At(), MouseButton.Left);
                    window.MouseUp(At(), MouseButton.Left);
                    break;
                case "focus":
                    focusTarget?.Focus();
                    break;
                case "call":
                    call?.Invoke(window);
                    break;
                case "key":
                    window.KeyPressQwerty(Enum.Parse<PhysicalKey>(action[1].GetString()!), RawInputModifiers.None);
                    window.KeyReleaseQwerty(Enum.Parse<PhysicalKey>(action[1].GetString()!), RawInputModifiers.None);
                    break;
                case "wait":
                    Thread.Sleep(action[1].GetInt32());
                    break;
            }
            Pump();
        }
        Pump();
        using var frame = window.CaptureRenderedFrame();
        frame?.Save(Path.Combine(output, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        window.Close();
        Pump();
    }

    private static void Pump()
    {
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    private static Window Host(int width, int height, Control content, IBrush? background = null) => new()
    {
        Width = width,
        Height = height,
        Background = background ?? T.AppBg,
        Content = content,
    };

    private static Control At(Control control, double x, double y)
    {
        control.HorizontalAlignment = HorizontalAlignment.Left;
        control.VerticalAlignment = VerticalAlignment.Top;
        control.Margin = new Thickness(x, y, 0, 0);
        return control;
    }

    private static Panel Fill(IBrush background, params Control[] children)
    {
        var panel = new Panel { Background = background };
        foreach (Control child in children)
            panel.Children.Add(child);
        return panel;
    }

    private static Window Build(string name, int w, int h, out Control? focus, out Action<Window>? call)
    {
        focus = null;
        call = null;
        if (name.StartsWith("wcbtn_"))
        {
            string kind = name.Split('_')[1];
            var button = new WindowControlButton { Kind = kind, Height = h };
            focus = button;
            return Host(w, h, Fill(T.ChromeRaised, button));
        }
        switch (name)
        {
            case "titlebar":
                return Host(w, h, new WindowTitleBar { TitleText = "HikariHopper — RPF Explorer", VerticalAlignment = VerticalAlignment.Top });
            case "titlebar_tool":
                return Host(w, h, new WindowTitleBar { Height = 24, TitleText = "ABOUT", ShowMinimize = false, ShowMaximize = false, VerticalAlignment = VerticalAlignment.Top });
            case "titlebar_long":
                return Host(w, h, new WindowTitleBar { TitleText = "HikariHopper — Grand Theft Auto V Enhanced edition with a long title", VerticalAlignment = VerticalAlignment.Top });
            case "icons16":
            {
                var wrap = new WrapPanel();
                foreach (string kind in new[] { "back", "forward", "caretUp", "caretDown", "up", "refresh", "list", "grid", "checkerboard", "check", "panel", "close", "plus", "minus", "search", "package" })
                    wrap.Children.Add(new ChromeIcon { Kind = kind, Width = 16, Height = 16, Stroke = T.Text });
                return Host(w, h, wrap);
            }
            case "icons_sizes":
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
                row.Children.Add(new ChromeIcon { Kind = "caretDown", Width = 14, Height = 14, Thickness = 1.6, VerticalAlignment = VerticalAlignment.Top });
                row.Children.Add(new ChromeIcon { Kind = "check", Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Top });
                row.Children.Add(new ChromeIcon { Kind = "package", Width = 17, Height = 17, Stroke = T.Brass, VerticalAlignment = VerticalAlignment.Top });
                row.Children.Add(new ChromeIcon { Kind = "search", Width = 13, Height = 13, Stroke = T.TextFaint, VerticalAlignment = VerticalAlignment.Top });
                row.Children.Add(new LucideIcon { IconName = "folder", Width = 34, Height = 34, Stroke = T.Accent, VerticalAlignment = VerticalAlignment.Top });
                return Host(w, h, row);
            }
            case "fileglyphs":
            {
                var wrap = new WrapPanel { ItemSpacing = 4, LineSpacing = 4 };
                wrap.Children.Add(new FileGlyph { Kind = "folder" });
                wrap.Children.Add(new FileGlyph { Kind = "archive" });
                wrap.Children.Add(new FileGlyph { Kind = "file", FileKind = "Drawable", Selected = true });
                string[] kinds = ["RPF Package", "Application", "Application Extension", "ASI Plugin", "Audio Container", "Texture Dictionary", "Drawable",
                    "Drawable Dictionary", "Fragment", "Map Data", "Collision Bounds", "Archetype Definitions", "Map Manifest", "Node Dictionary",
                    "Navigation Mesh", "Clip Dictionary", "Expression Dictionary", "Particle Dictionary", "Audio Metadata", "Text Table",
                    "Metadata Container", "Texture Parent Data", "Height Map", "Water Data", "Cutscene", "Vehicle Definitions", "Vehicle Handling",
                    "Vehicle Colors", "Vehicle Mod Colors", "Vehicle Variations", "Vehicle Layouts", "Ped Definitions", "Metadata", "XML Document",
                    "JSON Document", "Text Document", "Data File", "File", "Unknown"];
                foreach (string kind in kinds)
                    wrap.Children.Add(new FileGlyph { Kind = "file", FileKind = kind });
                return Host(w, h, wrap);
            }
            case "dotted":
            {
                var canvas = new Canvas();
                canvas.Children.Add(Place(new DottedRule { Height = 29 }, 2, 0));
                canvas.Children.Add(Place(new DottedRule { Width = 50, Horizontal = true }, 6, 3));
                canvas.Children.Add(Place(new DottedRule { Width = 51, Horizontal = true, Gap = 3, DotSize = 2, DotColor = T.Text }, 6, 8));
                return Host(w, h, canvas);
            }
            case "ctb_default_size":
                return Host(w, h, Fill(T.ChromeBg, At(new ChromeToolButton { IconKind = "back" }, 4, 4)));
            case "mbb_row":
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(1, 0, 0, 0) };
                foreach (string text in new[] { "File", "Edit", "View", "Go", "Help" })
                    row.Children.Add(new MenuBarButton { Text = text });
                var seam = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Background = T.BorderHard };
                return Host(w, h, Fill(T.ChromeBg, seam, row));
            }
            case "checkbox":
            case "checkbox_hover":
            {
                var column = new StackPanel { Spacing = 4, Margin = new Thickness(4), HorizontalAlignment = HorizontalAlignment.Left };
                column.Children.Add(new SquareCheckBox { Text = "Generate mipmaps" });
                column.Children.Add(new SquareCheckBox { Text = "Repair alpha edges", IsChecked = true });
                return Host(w, h, column);
            }
            case "checkbox_disabled":
                return Host(w, h, At(new SquareCheckBox { Text = "Disabled option", IsEnabled = false, IsChecked = true }, 4, 4));
            case "scroll":
            case "scroll_hover":
            case "scroll_pressed":
            case "scroll_track_click":
            case "scroll_none":
            {
                int count = name == "scroll_none" ? 3 : 60;
                var rows = new StackPanel();
                for (int i = 0; i < count; i++)
                    rows.Children.Add(new TextBlock { Height = 26, Text = "Row " + i, Foreground = T.TextRow, FontFamily = T.MonoFont, FontSize = 12 });
                var viewer = new ScrollViewer { Content = rows };
                viewer.Theme = (Avalonia.Styling.ControlTheme)Application.Current!.FindResource("QuietScrollViewer")!;
                return Host(w, h, viewer);
            }
            case "split":
            case "split_hover":
            case "split_pressed":
            case "split_hover_on":
            case "split_hover_edge":
            {
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("60,Auto,*") };
                grid.Children.Add(new Border { Background = T.SidebarBg });
                var handle = new SplitViewHandle();
                Grid.SetColumn(handle, 1);
                var right = new Border { Background = T.AppBg };
                Grid.SetColumn(right, 2);
                grid.Children.Add(right);
                grid.Children.Add(handle);
                return Host(w, h, grid);
            }
            case "settings_header":
                return Host(w, h, new SettingsPanelHeader { Label = "OPTIONS  /  GAME PATHS", VerticalAlignment = VerticalAlignment.Top });
            case "settings_group_rows":
            {
                var column = new StackPanel { Background = T.SidebarBg };
                column.Children.Add(new SettingsGroupRow { Label = "Game paths", Note = "9 titles", Selected = true });
                column.Children.Add(new SettingsGroupRow { Label = "Explorer", Note = "Later", Available = false });
                column.Children.Add(new SettingsGroupRow { Label = "Appearance", Note = "Later", Available = false });
                return Host(w, h, column);
            }
            case "gamepath_rows":
            {
                var column = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
                column.Children.Add(new GamePathRow { GameName = "Grand Theft Auto V Legacy", GamePath = @"D:\Games\GTAV", ExpectedExecutable = "GTA5.exe", Supported = true, PathValid = true });
                column.Children.Add(new GamePathRow { GameName = "Grand Theft Auto V Enhanced", GamePath = @"C:\Nope", ExpectedExecutable = "GTA5_Enhanced.exe", Supported = true, PathValid = false });
                column.Children.Add(new GamePathRow { GameName = "Grand Theft Auto IV" });
                return Host(w, h, column);
            }
            case "about":
                return new AboutWindow();
            case "shortcuts":
                return new ShortcutsWindow();
            case "settings":
                return new SettingsWindow(TestGamePaths());
            case "splash":
                return new SplashScreen { StartupMessage = "Loading FiveFury runtime and GTA V keys…", StartupPhase = "GAME" };
            case "splash_boot":
                return new SplashScreen();
        }
        if (name.StartsWith("ctb_"))
            return ChromeButtonScenario(name, w, h, out focus);
        if (name.StartsWith("mbb"))
        {
            var button = new MenuBarButton { Text = "File", MenuOpen = name == "mbb_open", HorizontalAlignment = HorizontalAlignment.Left };
            focus = button;
            return Host(w, h, Fill(T.ChromeBg, button));
        }
        if (name == "ftf_tab")
        {
            var column = new StackPanel { Margin = new Thickness(4), Spacing = 6, HorizontalAlignment = HorizontalAlignment.Left };
            column.Children.Add(new FlatTextField { Width = 300, Height = 28, Text = "first" });
            column.Children.Add(new FlatTextField { Width = 300, Height = 28, Text = "second value" });
            return Host(w, h, Fill(T.ChromeBg, column));
        }
        if (name == "ftf_click")
            return Host(w, h, Fill(T.ChromeBg, At(new FlatTextField { Width = 300, Height = 28, Text = "texture_01" }, 4, 4)));
        if (name.StartsWith("ftf_"))
            return TextFieldScenario(name, w, h, out focus);
        if (name.StartsWith("fcb"))
        {
            var combo = new FlatComboBox
            {
                Width = 300,
                ItemsSource = new[]
                {
                    new FlatComboOption("Grand Theft Auto V Legacy"),
                    new FlatComboOption("Grand Theft Auto V Enhanced"),
                    new FlatComboOption("Grand Theft Auto IV", Supported: false, Note: "No keys"),
                },
                SelectedIndex = name == "fcb_unsupported" ? 2 : 0,
            };
            return Host(w, h, Fill(T.ChromeBg, At(combo, 4, 4)));
        }
        if (name == "menu_long")
        {
            var menu = new RetroMenu();
            menu.Items.Add(new RetroMenuItem { Text = "A very long menu item text that exceeds the minimum width", ShortcutText = "Ctrl+Shift+Alt+F12" });
            menu.Items.Add(new RetroMenuItem { Text = "Short" });
            var root = new Panel { Background = T.ChromeBg };
            call = _ => menu.Open(root, 1, 4);
            return Host(w, h, root, T.ChromeBg);
        }
        if (name.StartsWith("menu"))
            return MenuScenario(w, h, out call);
        throw new InvalidOperationException("No Avalonia builder for scenario " + name);
    }

    private static Control Place(Control control, double x, double y)
    {
        Canvas.SetLeft(control, x);
        Canvas.SetTop(control, y);
        return control;
    }

    private static Window ChromeButtonScenario(string name, int w, int h, out Control? focus)
    {
        string variant = name.Split('_')[1];
        var button = new ChromeToolButton { Width = 92, Height = 28 };
        switch (variant)
        {
            case "ghost": button.Text = "Close"; break;
            case "raised": button.Raised = true; button.Text = "Close"; break;
            case "primary": button.Primary = true; button.Text = "Open"; break;
            case "destructive": button.Destructive = true; button.Text = "Delete"; break;
            case "sunken": button.Sunken = true; button.IconKind = "list"; break;
            case "icon": button.IconKind = "refresh"; button.Bordered = false; break;
            case "nofill": button.IconKind = "close"; button.HoverFill = false; button.Bordered = false; break;
        }
        if (name.EndsWith("_disabled"))
            button.IsEnabled = false;
        focus = button;
        return Host(w, h, Fill(T.ChromeBg, At(button, 4, 4)));
    }

    private static Window TextFieldScenario(string name, int w, int h, out Control? focus)
    {
        var field = new FlatTextField { Width = 300, Height = 28 };
        switch (name)
        {
            case "ftf_placeholder": field.PlaceholderText = "Select the installation folder"; break;
            case "ftf_text": field.Text = LegacyPath; break;
            case "ftf_search":
            case "ftf_hover":
                field.PlaceholderText = "Search this folder"; field.LeadingIcon = "search"; field.KeyCap = "F3"; break;
            case "ftf_invalid": field.Text = "bad name?"; field.Invalid = true; break;
            case "ftf_focus": field.Text = "texture_01"; break;
            case "ftf_disabled": field.PlaceholderText = "Future support"; field.IsEnabled = false; field.Opacity = 0.48; break;
        }
        focus = field;
        return Host(w, h, Fill(T.ChromeBg, At(field, 4, 4)));
    }

    private static Window MenuScenario(int w, int h, out Action<Window>? call)
    {
        var button = new MenuBarButton { Text = "File", HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(1, 0, 0, 0) };
        var menu = new RetroMenu();
        menu.Items.Add(new RetroMenuItem { Text = "New tab", ShortcutText = "Ctrl+T" });
        menu.Items.Add(new RetroMenuSeparator());
        menu.Items.Add(new RetroMenuItem { Text = "Open RPF archive…", ShortcutText = "Ctrl+Shift+O" });
        menu.Items.Add(new RetroMenuItem { Text = "Close RPF archive", IsEnabled = false });
        menu.Items.Add(new RetroMenuItem { Text = "List view", Checkable = true, IsChecked = true });
        menu.Items.Add(new RetroMenuItem { Text = "Grid view", Checkable = true });
        menu.Items.Add(new RetroMenuItem { Text = "Folder", IconName = "folder" });
        menu.Items.Add(new RetroMenuSeparator());
        menu.Items.Add(new RetroMenuSection { Text = "RPF ARCHIVE" });
        var sub = new RetroMenuItem { Text = "New", IconName = "plus" };
        sub.Items.Add(new RetroMenuItem { Text = "Folder" });
        menu.Items.Add(sub);
        menu.Items.Add(new RetroMenuItem { Text = "Exit", ShortcutText = "Alt+F4" });
        menu.Opened += (_, _) => button.MenuOpen = true;
        menu.Closed += (_, _) => button.MenuOpen = false;
        var root = Fill(T.ChromeBg, button);
        call = _ =>
        {
            menu.ConnectionWidth = button.Bounds.Width;
            menu.Open(root, 1, 25);
        };
        return Host(w, h, root, T.ChromeBg);
    }

    /// <summary>A GamePathSettings reading a scratch registry key with the scenario's paths.</summary>
    private static GamePathSettings TestGamePaths()
    {
        string original = AppSettings.RegistryPath;
        AppSettings.RegistryPath = @"Software\HikariHopper\RenderScenarios";
        try
        {
            AppSettings.SetString(GameSettings.LegacyGameRootKey, LegacyPath);
            AppSettings.Remove(GameSettings.EnhancedGameRootKey);
            return new GamePathSettings();
        }
        finally
        {
            AppSettings.RegistryPath = original;
        }
    }
}
