using HikariHopper.Core;
using HikariHopper.ViewModels;

namespace HikariHopper.Tests;

/// <summary>Ports of <c>tests/test_settings.py</c>, <c>tests/test_start_page.py</c> and <c>tests/test_startup.py</c> (logic).</summary>
[Collection(ExplorerCollection.Name)]
public sealed class GameSettingsTests : ExplorerTestBase
{
    [AvaloniaFact]
    public void GameEditionDetectionPrefersEnhanced()
    {
        string both = MakeGame("Both");
        File.WriteAllText(Path.Combine(both, "GTA5_Enhanced.exe"), "enhanced");
        Assert.Equal(GameSettings.EnhancedEdition, GameSettings.GameEditionForPath(both));
        Assert.Equal("", GameSettings.GameEditionForPath(Path.Combine(Work, "missing")));
    }

    [AvaloniaFact]
    public void StartupSplashRequiresARestorableGame()
    {
        Assert.Equal("", GameSettings.ConfiguredGameRootToOpen());
        Assert.False(ExplorerTabsViewModel.StartupSplashRequired());

        AppSettings.SetString(GameSettings.LastGameRootKey, Path.Combine(Work, "Missing"));
        Assert.False(ExplorerTabsViewModel.StartupSplashRequired());

        string enhanced = MakeGame("Enhanced", "GTA5_Enhanced.exe");
        AppSettings.SetString(GameSettings.LastGameRootKey, enhanced);
        Assert.Equal(enhanced, GameSettings.ConfiguredGameRootToOpen());
        Assert.True(ExplorerTabsViewModel.StartupSplashRequired());
    }

    [AvaloniaFact]
    public void RememberGameRootTracksEachEdition()
    {
        string legacy = MakeGame("Legacy");
        string enhanced = MakeGame("Enhanced", "GTA5_Enhanced.exe");
        GameSettings.RememberGameRoot(legacy);
        GameSettings.RememberGameRoot(enhanced);
        Assert.Equal(legacy, AppSettings.GetString(GameSettings.LegacyGameRootKey));
        Assert.Equal(enhanced, AppSettings.GetString(GameSettings.EnhancedGameRootKey));
        Assert.Equal(enhanced, AppSettings.GetString(GameSettings.LastGameRootKey));
    }

    [AvaloniaFact]
    public void SchemaThreeMigratesTheLastGameToItsEdition()
    {
        string legacy = MakeGame("Legacy");
        AppSettings.SetInt("settingsSchemaVersion", 1);
        AppSettings.SetString(GameSettings.LastGameRootKey, legacy);
        GameSettings.MigrateLegacySettings();
        Assert.Equal(3, AppSettings.GetInt("settingsSchemaVersion"));
        Assert.Equal(legacy, AppSettings.GetString(GameSettings.LegacyGameRootKey));
    }

    [AvaloniaFact]
    public void SchemaThreeUsesAConfiguredPathWhenNoLastGameExists()
    {
        string enhanced = MakeGame("Enhanced", "GTA5_Enhanced.exe");
        AppSettings.SetInt("settingsSchemaVersion", 2);
        AppSettings.SetString(GameSettings.EnhancedGameRootKey, enhanced);
        GameSettings.MigrateLegacySettings();
        Assert.Equal(3, AppSettings.GetInt("settingsSchemaVersion"));
        Assert.Equal(enhanced, AppSettings.GetString(GameSettings.LastGameRootKey));
    }

    [AvaloniaFact]
    public void GamePathSettingsPersistsAndValidatesPaths()
    {
        string legacy = MakeGame("Legacy");
        string invalid = Directory.CreateDirectory(Path.Combine(Work, "NotAGame")).FullName;
        var controller = new GamePathSettings();
        int changes = 0;
        controller.PathsChanged += (_, _) => changes++;

        controller.SetLegacyPath($"  \"{legacy}\"  ");
        controller.SetEnhancedPath(invalid);

        Assert.Equal(legacy, controller.LegacyPath);
        Assert.True(controller.LegacyPathValid);
        Assert.Equal(legacy, AppSettings.GetString(GameSettings.LegacyGameRootKey));
        Assert.Equal(legacy, AppSettings.GetString(GameSettings.LastGameRootKey));
        Assert.Equal(invalid, controller.EnhancedPath);
        Assert.False(controller.EnhancedPathValid);
        Assert.Equal(invalid, AppSettings.GetString(GameSettings.EnhancedGameRootKey));
        Assert.Equal(2, changes);

        controller.SetEnhancedPath("");
        Assert.Equal("", AppSettings.GetString(GameSettings.EnhancedGameRootKey));
        Assert.Equal(3, changes);

        controller.SetLegacyPath("");
        Assert.Equal("", AppSettings.GetString(GameSettings.LegacyGameRootKey));
        Assert.Equal("", AppSettings.GetString(GameSettings.LastGameRootKey));
        Assert.Equal(4, changes);
    }

    [AvaloniaFact]
    public void ConfiguredGameOpensWithoutAFolderDialog() => OnUiThread(() =>
    {
        string legacy = MakeGame("Legacy");
        AppSettings.SetString(GameSettings.LegacyGameRootKey, legacy);
        var bridge = new ExplorerViewModel();
        var opened = new List<string>();
        bridge.GameOpened += (_, path) => opened.Add(path);

        bridge.OpenConfiguredGame(GameSettings.LegacyEdition);
        Assert.True(bridge.GameLoading);
        Assert.Equal("Grand Theft Auto V Legacy", bridge.GameLoadingName);
        Assert.Equal("Loading Grand Theft Auto V Legacy…", bridge.Status);
        Settle(bridge);

        Assert.Equal([legacy], opened);
        Assert.True(bridge.HasGame);
        Assert.False(bridge.GameLoading);
        Assert.Equal("", bridge.GameLoadingName);
        Assert.Equal(legacy, bridge.GamePath);
        Assert.Empty(Dialogs.Requests);
    });

    [AvaloniaFact]
    public void ManualGameLoadingKeepsTheCallingThreadResponsive() => OnUiThread(() =>
    {
        string legacy = MakeGame("Legacy");
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        LoadHook = path =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(5));
            return FakeLoadedGame(path);
        };
        var bridge = new ExplorerViewModel();

        Assert.True(bridge.OpenGameAsync(legacy));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(bridge.GameLoading);
        Assert.True(bridge.EntryOperationBusy);
        Assert.False(bridge.HasGame);
        Assert.False(bridge.OpenGameAsync(legacy));

        release.Set();
        Settle(bridge);

        Assert.True(bridge.HasGame);
        Assert.Equal(legacy, bridge.GamePath);
        Assert.False(bridge.GameLoading);
        Assert.False(bridge.EntryOperationBusy);
        Assert.Equal("Loaded GTA V Legacy", bridge.Status);
    });

    [AvaloniaFact]
    public void FailedGameLoadReportsTheError() => OnUiThread(() =>
    {
        string folder = Directory.CreateDirectory(Path.Combine(Work, "Nothing")).FullName;
        var bridge = new ExplorerViewModel();
        Assert.True(bridge.OpenGameAsync(folder));
        Assert.Equal("Nothing", bridge.GameLoadingName);
        Settle(bridge);
        Assert.Equal("Could not load game: Select the GTA V folder containing GTA5.exe or GTA5_Enhanced.exe", bridge.Status);
        Assert.False(bridge.GameLoading);
        Assert.False(bridge.HasGame);
    });

    [AvaloniaFact]
    public void MissingConfiguredGamePointsToSettings() => OnUiThread(() =>
    {
        var bridge = new ExplorerViewModel();
        bridge.OpenConfiguredGame(GameSettings.EnhancedEdition);
        Assert.Equal("Configure the GTA V Enhanced path in Settings", bridge.Status);
        bridge.OpenConfiguredGame();
        Assert.Equal("Configure the game path in Settings", bridge.Status);
    });

    [AvaloniaFact]
    public void RestoreLastGameOpensTheSavedInstallation() => OnUiThread(() =>
    {
        string enhanced = MakeGame("Enhanced", "GTA5_Enhanced.exe");
        AppSettings.SetString(GameSettings.LastGameRootKey, enhanced);
        var tabs = new ExplorerTabsViewModel();
        tabs.RestoreLastGame();
        Assert.True(tabs.ActiveBridge.HasGame);
        Assert.Equal(enhanced, tabs.ActiveBridge.GamePath);
        Assert.Equal("Home", tabs.ActiveBridge.TabTitle);
    });

    [AvaloniaFact]
    public void TabsRefreshGamePathSettingsAndRecentGamesAfterOpeningAGame() => OnUiThread(() =>
    {
        string enhanced = MakeGame("Enhanced", "GTA5_Enhanced.exe");
        string legacy = MakeGame("Legacy");
        var tabs = new ExplorerTabsViewModel();
        int changes = 0;
        tabs.GamePathSettings.PathsChanged += (_, _) => changes++;
        var notified = new List<string?>();
        tabs.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        tabs.ActiveBridge.OpenGame(enhanced);

        Assert.Equal(enhanced, tabs.GamePathSettings.EnhancedPath);
        Assert.True(tabs.GamePathSettings.EnhancedPathValid);
        Assert.Equal(1, changes);
        Assert.Contains(nameof(ExplorerTabsViewModel.RecentGames), notified);
        Assert.Contains(nameof(ExplorerTabsViewModel.ActiveBridge), notified);
        Assert.Equal([new RecentGame("Enhanced", enhanced, "GTA V Enhanced")], tabs.RecentGames);

        tabs.OpenRecentGame(legacy);
        Settle(tabs);
        tabs.ActiveBridge.OpenGame(enhanced);
        Assert.Equal([enhanced, legacy], AppSettings.GetStringList(GameSettings.RecentGameRootsKey));
        Assert.Equal(["GTA V Enhanced", "GTA V Legacy"], tabs.RecentGames.Select(game => game.Edition));

        Directory.Delete(legacy, recursive: true);
        Assert.Single(tabs.RecentGames);
    });

    [AvaloniaFact]
    public void RecentGamesKeepTheSixNewest() => OnUiThread(() =>
    {
        var tabs = new ExplorerTabsViewModel();
        var roots = Enumerable.Range(0, 8).Select(index => MakeGame($"Game{index}")).ToList();
        foreach (string root in roots)
            tabs.ActiveBridge.OpenGame(root);
        Assert.Equal(roots.AsEnumerable().Reverse().Take(6), AppSettings.GetStringList(GameSettings.RecentGameRootsKey));
    });
}
