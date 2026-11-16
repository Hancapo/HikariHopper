using HikariHopper.Core;

namespace HikariHopper.Tests;

public sealed class GameCatalogTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "hh-catalog-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    // An installation of an edition: a folder named as Steam names it, with that edition's executable.
    private string Install(string folder, string edition)
    {
        string path = Path.Combine(root, folder);
        Directory.CreateDirectory(path);
        File.WriteAllBytes(Path.Combine(path, GameSettings.GameExecutable(edition)), []);
        return path;
    }

    [Fact]
    public void Each_editions_facts_come_from_the_catalog()
    {
        GameEditionInfo legacy = GameCatalog.Edition(GameSettings.LegacyEdition)!;
        Assert.Equal(GameCatalog.GtaV, legacy.Game.Id);
        Assert.Equal("GTA V Legacy", legacy.ShortName);
        Assert.Equal("Grand Theft Auto V Legacy", legacy.FullName);
        Assert.Equal("Select GTA V Enhanced Installation", GameSettings.BrowseTitle(GameSettings.EnhancedEdition));
        Assert.Equal("GTA5.exe", GameSettings.GameExecutable(GameSettings.LegacyEdition));
        Assert.Equal(GameSettings.EnhancedGameRootKey, GameSettings.GameRootKey(GameSettings.EnhancedEdition));
        Assert.Equal([GameSettings.EnhancedEdition, GameSettings.LegacyEdition], GameSettings.AllEditions);
        Assert.Null(GameCatalog.Edition(""));

        // A folder is the first edition whose executable it holds: with both, Enhanced.
        string both = Install("Grand Theft Auto V", GameSettings.LegacyEdition);
        Assert.Equal(GameSettings.LegacyEdition, GameSettings.GameEditionForPath(both));
        File.WriteAllBytes(Path.Combine(both, "GTA5_Enhanced.exe"), []);
        Assert.Equal(GameSettings.EnhancedEdition, GameSettings.GameEditionForPath(both));
        Assert.Equal("", GameSettings.GameEditionForPath(root));
    }

    [Fact]
    public void A_recent_installation_is_named_for_its_edition_as_a_configured_one_is()
    {
        string enhanced = Install("Grand Theft Auto V Enhanced", GameSettings.EnhancedEdition);
        string legacy = Install("Grand Theft Auto V", GameSettings.LegacyEdition);

        // Enhanced set up in Settings, Legacy only opened before.
        var (_, entries) = Assert.Single(GameCatalog.Launcher(
            edition => edition == GameSettings.EnhancedEdition ? enhanced : "", [legacy]));
        Assert.Equal(["Enhanced", "Legacy"], entries.Select(entry => entry.Title));
        Assert.Equal([enhanced, legacy], entries.Select(entry => entry.Path));
    }

    [Fact]
    public void A_second_installation_of_an_edition_is_named_for_its_folder()
    {
        string first = Install("Grand Theft Auto V", GameSettings.LegacyEdition);
        string second = Install("GTA V modded", GameSettings.LegacyEdition);

        var (_, entries) = Assert.Single(GameCatalog.Launcher(_ => "", [first, second]));
        Assert.Equal(["Legacy", "GTA V modded"], entries.Select(entry => entry.Title));
    }
}
