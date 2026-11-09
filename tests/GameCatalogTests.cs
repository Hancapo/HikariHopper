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
