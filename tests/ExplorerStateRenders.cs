using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HikariHopper.Core;
using HikariHopper.ViewModels;
using HikariHopper.Views;

namespace HikariHopper.Tests;

/// <summary>
/// Renders the explorer comparison states (the same list the Qt driver renders from main.qml) with
/// the real <see cref="ExplorerTabsViewModel"/> against the Legacy install. Opt-in: set
/// <c>HH_STATES</c> to the states.json, <c>HH_STATES_OUT</c> to an output folder and
/// <c>HIKARI_TEST_LEGACY</c> to the game; <c>HH_ONLY</c> limits it to a comma list of names.
/// Settings go to a scratch registry key seeded like the Qt side; nothing is written to the game.
/// </summary>
[Collection(ExplorerCollection.Name)]
public sealed class ExplorerStateRenders
{
    private const string EnhancedPath = @"C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced";

    // A tool for PR screenshots, not a test: run only when asked for (--explicit only).
    [AvaloniaFact(Explicit = true)]
    public void RenderAll()
    {
        string? states = Environment.GetEnvironmentVariable("HH_STATES");
        string? output = Environment.GetEnvironmentVariable("HH_STATES_OUT");
        string? legacy = Environment.GetEnvironmentVariable("HIKARI_TEST_LEGACY");
        if (string.IsNullOrEmpty(states) || string.IsNullOrEmpty(output) || string.IsNullOrEmpty(legacy))
            Assert.Skip("Set HH_STATES, HH_STATES_OUT and HIKARI_TEST_LEGACY to render the explorer states.");
        string[]? only = Environment.GetEnvironmentVariable("HH_ONLY")?.Split(',', StringSplitOptions.RemoveEmptyEntries);
        Directory.CreateDirectory(output);

        string previousRegistry = AppSettings.RegistryPath;
        Func<string, LoadedGame> previousLoader = ExplorerViewModel.GameLoader;
        AppSettings.RegistryPath = $@"Software\HikariHopperTests\States{Guid.NewGuid():N}";
        var failures = new List<string>();
        try
        {
            AppSettings.SetInt("settingsSchemaVersion", 3);
            AppSettings.SetString(GameSettings.LastGameRootKey, legacy);
            AppSettings.SetString(GameSettings.LegacyGameRootKey, legacy);
            AppSettings.SetString(GameSettings.EnhancedGameRootKey, EnhancedPath);
            AppSettings.SetStringList(GameSettings.RecentGameRootsKey, [legacy, EnhancedPath]);
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(states));
            foreach (JsonElement state in document.RootElement.EnumerateArray())
            {
                string name = state.GetProperty("name").GetString()!;
                if (only is not null && !only.Contains(name))
                    continue;
                try
                {
                    Render(state, name, output, legacy);
                }
                catch (Exception error)
                {
                    failures.Add($"{name}: {error}");
                }
                finally
                {
                    ExplorerViewModel.GameLoader = previousLoader;
                }
            }
        }
        finally
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(AppSettings.RegistryPath, throwOnMissingSubKey: false);
            AppSettings.RegistryPath = previousRegistry;
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    private static void Render(JsonElement state, string name, string output, string legacy)
    {
        var session = new ExplorerSession();
        var window = new MainWindow
        {
            Width = state.GetProperty("w").GetInt32(),
            Height = state.GetProperty("h").GetInt32(),
        };
        session.Attach(window);
        window.Show();
        TestWait.Pump(150);
        MouseButton? held = null;
        Point heldAt = default;
        foreach (JsonElement op in state.GetProperty("ops").EnumerateArray())
        {
            string kind = op[0].GetString()!;
            ExplorerViewModel bridge = session.Tabs.ActiveBridge;
            switch (kind)
            {
                case "open_game":
                    bridge.OpenGame(legacy);
                    break;
                case "open_enhanced":
                    bridge.OpenGame(EnhancedPath);
                    break;
                case "activate":
                    bridge.ActivateEntry(RowOf(bridge, op[1].GetString()!));
                    break;
                case "select":
                    bridge.SelectEntry(RowOf(bridge, op[1].GetString()!), Modifiers(op[2].GetInt32()));
                    break;
                case "view":
                    bridge.SetViewMode(op[1].GetString()!);
                    break;
                case "search":
                {
                    // The folder's box opens the search row on the folder; the text goes to its query.
                    TextBox field = window.GetVisualDescendants().OfType<NavigationBar>().Single().FindControl<TextBox>("SearchField")!;
                    field.Focus();
                    Dispatcher.UIThread.RunJobs();
                    window.KeyTextInput(op[1].GetString()!);
                    break;
                }
                case "toggle_tree":
                    bridge.ToggleTreeNode(op[1].GetString()!);
                    break;
                case "focus_tree":
                    bridge.FocusTreePath(op[1].GetString()!);
                    break;
                case "press":
                case "release":
                case "click":
                {
                    var point = new Point(op[1].GetDouble(), op[2].GetDouble());
                    MouseButton button = op[3].GetString() == "right" ? MouseButton.Right : MouseButton.Left;
                    if (kind != "release")
                    {
                        window.MouseMove(point);
                        window.MouseDown(point, button);
                        held = button;
                        heldAt = point;
                    }
                    if (kind != "press")
                    {
                        TestWait.Pump(20);
                        window.MouseUp(point, button);
                        held = null;
                    }
                    break;
                }
                case "move":
                    heldAt = new Point(op[1].GetDouble(), op[2].GetDouble());
                    window.MouseMove(heldAt, held == MouseButton.Left ? RawInputModifiers.LeftMouseButton : RawInputModifiers.None);
                    break;
                case "key":
                {
                    PhysicalKey key = op[1].GetString() switch
                    {
                        "Down" => PhysicalKey.ArrowDown,
                        "Up" => PhysicalKey.ArrowUp,
                        "Left" => PhysicalKey.ArrowLeft,
                        "Right" => PhysicalKey.ArrowRight,
                        "F3" => PhysicalKey.F3,
                        var other => Enum.Parse<PhysicalKey>(other!),
                    };
                    RawInputModifiers modifiers = op[2].GetInt32() switch
                    {
                        1 => RawInputModifiers.Control,
                        2 => RawInputModifiers.Shift,
                        _ => RawInputModifiers.None,
                    };
                    window.KeyPressQwerty(key, modifiers);
                    window.KeyReleaseQwerty(key, modifiers);
                    break;
                }
                case "create_dialog":
                    window.GetVisualDescendants().OfType<WorkspaceView>().Single()
                        .OpenCreateDialog(op[1].GetString()!, op[2].GetString()!, op[3].GetString()!);
                    break;
                case "delete_request":
                    bridge.RequestDeleteSelection();
                    break;
                case "refresh":
                    bridge.Refresh();
                    break;
                case "slow_load":
                {
                    Func<string, LoadedGame> original = ExplorerViewModel.GameLoader;
                    ExplorerViewModel.GameLoader = path =>
                    {
                        Thread.Sleep(4000);
                        return original(path);
                    };
                    bridge.OpenConfiguredGame("");
                    break;
                }
                case "new_tab":
                    session.Tabs.NewTab();
                    break;
                case "open_archive":
                    bridge.OpenArchive(Path.Combine(legacy, op[1].GetString()!));
                    break;
                case "activate_tab":
                    session.Tabs.ActivateTab(op[1].GetInt32());
                    break;
                case "folders":
                    bridge.SetFoldersVisible(op[1].GetBoolean());
                    break;
                case "wait":
                    TestWait.Pump(op[1].GetInt32());
                    break;
                case "wheel":
                {
                    var point = new Point(op[1].GetDouble(), op[2].GetDouble());
                    window.MouseMove(point);
                    window.MouseWheel(point, new Vector(0, op[3].GetDouble()));
                    break;
                }
            }
            TestWait.Pump(80);
        }
        TestWait.Pump(250);
        using (var frame = window.CaptureRenderedFrame())
            frame?.Save(Path.Combine(output, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        if (held is { } button2)
            window.MouseUp(heldAt, button2);
        window.Close();
        // Let a slow load finish before the next state starts.
        TestWait.Pump(state.GetProperty("ops").EnumerateArray().Any(op => op[0].GetString() == "slow_load") ? 4500 : 30);
    }

    private static int RowOf(ExplorerViewModel bridge, string name)
    {
        for (int row = 0; row < bridge.EntriesModel.Count; row++)
        {
            if (bridge.EntriesModel[row].Name == name)
                return row;
        }
        throw new InvalidOperationException($"No entry {name}");
    }

    private static KeyModifiers Modifiers(int value) => value switch
    {
        1 => KeyModifiers.Control,
        2 => KeyModifiers.Shift,
        _ => KeyModifiers.None,
    };
}
