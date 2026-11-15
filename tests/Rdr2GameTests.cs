using HikariHopper.Core;
using HikariHopper.ViewModels;
using HikariHopper.Views;

namespace HikariHopper.Tests;

/// <summary>Red Dead Redemption 2 as a game HikariHopper knows: its catalog entry, its keys, and opening it.</summary>
[Collection(ExplorerCollection.Name)]
public sealed class Rdr2GameTests : ExplorerTestBase
{
    // A folder with RDR2's executable, not a real one: of no version the keys open.
    private string FakeRdr2(string folder = "Red Dead Redemption 2")
    {
        string path = Directory.CreateDirectory(Path.Combine(Work, folder)).FullName;
        File.WriteAllBytes(Path.Combine(path, "RDR2.exe"), []);
        return path;
    }

    [Fact]
    public void RDR2_is_a_game_of_one_edition_in_the_catalog()
    {
        GameEditionInfo rdr2 = GameCatalog.Edition(GameSettings.Rdr2Edition)!;
        Assert.Equal(GameCatalog.Rdr2, rdr2.Game.Id);
        Assert.Single(rdr2.Game.Editions);
        Assert.Equal("RDR2", rdr2.ShortName);
        Assert.Equal("Red Dead Redemption 2", rdr2.FullName);
        Assert.Equal("Select RDR2 Installation", rdr2.BrowseTitle);
        Assert.Equal("RDR2.exe", GameSettings.GameExecutable(GameSettings.Rdr2Edition));
        Assert.Equal(GameSettings.Rdr2GameRootKey, GameSettings.GameRootKey(GameSettings.Rdr2Edition));
        // After GTA V's, which the start page and Settings keep first.
        Assert.Equal([GameSettings.EnhancedEdition, GameSettings.LegacyEdition, GameSettings.Rdr2Edition], GameSettings.AllEditions);
        Assert.Equal(GameSettings.Rdr2Edition, GameSettings.GameEditionForPath(FakeRdr2()));
    }

    [Fact]
    public void Its_start_page_row_is_named_for_the_store_it_came_from()
    {
        Assert.Equal("Steam", GameCatalog.StoreName(@"C:\Program Files (x86)\Steam\steamapps\common\Red Dead Redemption 2"));
        Assert.Equal("Epic Games", GameCatalog.StoreName(@"D:\Epic Games\RedDeadRedemption2"));
        Assert.Equal("Rockstar Games Launcher", GameCatalog.StoreName(@"C:\Program Files\Rockstar Games\Red Dead Redemption 2"));
        Assert.Equal("PC", GameCatalog.StoreName(@"E:\Games\RDR2"));

        string installed = FakeRdr2();
        var groups = GameCatalog.Launcher(edition => edition == GameSettings.Rdr2Edition ? installed : "", []);
        var (game, entries) = Assert.Single(groups, group => group.Game.Id == GameCatalog.Rdr2);
        LauncherEntry row = Assert.Single(entries);
        Assert.Equal("PC", row.Title);
        Assert.Equal(GameSettings.Rdr2Edition, row.Edition);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void A_build_the_shipped_keys_dont_open_is_said_before_it_is_opened()
    {
        string fake = FakeRdr2();
        Assert.Equal("RDR2.exe of an unknown version found · HikariHopper opens 1.0.1491.50",
            GameSettings.InstallationProblem(fake, GameSettings.Rdr2Edition));
        // GTA V has no such check.
        Assert.Null(GameSettings.InstallationProblem(fake, GameSettings.LegacyEdition));
        // Opening it says the same, rather than failing on its archives.
        var error = Assert.Throws<ArgumentException>(() => RpfProvider.LoadGame(fake));
        Assert.Equal("RDR2.exe of an unknown version found · HikariHopper opens 1.0.1491.50", error.Message);

        // Settings says it in place of the check, as a warning.
        var row = new GamePathRow { Title = "Red Dead Redemption 2", Edition = GameSettings.Rdr2Edition, ExpectedExecutable = "RDR2.exe" };
        row.GamePath = fake;
        row.PathValid = true;
        Assert.Equal("✓ RDR2.exe found", row.StatusMessage);
        row.Problem = GameSettings.InstallationProblem(fake, GameSettings.Rdr2Edition)!;
        Assert.Equal("! RDR2.exe of an unknown version found · HikariHopper opens 1.0.1491.50", row.StatusMessage);
        Assert.Equal(HikariHopper.Ui.Theme.Warning, row.StatusText.Foreground);
        Assert.True(row.Mark.IsRdr2);
        Assert.Equal(HikariHopper.Ui.Theme.Rdr2Mark, row.Mark.Fill);
    }

    [Fact]
    public void The_shipped_keys_load()
    {
        Rdr2Keys keys = Rdr2Keys.Bundled;
        Assert.Same(keys, Rdr2Keys.Bundled);
        Assert.Equal(166, keys.Crypto.KeyCount);
        // An RDR2 game isn't GTA V: FiveFury's view of it isn't there.
        var game = new LoadedGame(Work, GameSettings.Rdr2Edition, keys);
        Assert.False(game.IsGtaV);
        Assert.Throws<InvalidOperationException>(() => game.Target);
        Assert.Throws<InvalidOperationException>(() => game.Crypto);
    }

    [Fact]
    public void The_installed_game_loads_with_the_shipped_keys()
    {
        string root = TestGames.Rdr2();
        Assert.Null(GameSettings.InstallationProblem(root, GameSettings.Rdr2Edition));
        LoadedGame game = RpfProvider.LoadGame(root);
        Assert.Equal(GameSettings.Rdr2Edition, game.Edition);
        Assert.Same(Rdr2Keys.Bundled, game.Keys);

        // Opened in the explorer: its folder, its name and its mark, and none of GTA V's view of it.
        var provider = new RpfProvider();
        provider.ApplyLoadedGame(game);
        Assert.Equal(GameSettings.Rdr2Edition, provider.GameEditionId);
        Assert.Equal("RDR2", provider.GameEdition);
        Assert.Same(game.Keys, provider.Keys);
        Assert.Null(provider.GameTarget);
        Assert.Null(provider.Crypto);
        Assert.Contains(provider.Entries(), entry => entry.Name.Equals("RDR2.exe", StringComparison.OrdinalIgnoreCase));
    }
}
